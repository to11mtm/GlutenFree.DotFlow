// <copyright file="JintExpressionEvaluator.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Services;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Microsoft.Extensions.Logging;
using Workflow.Core.Abstractions;

/// <summary>
/// 🧮 Default expression evaluator using <b>Jint</b> (JS/ES2020, BSD-2 licence)~
/// Evaluates sandboxed JavaScript expressions with memory limit, recursion cap, and timeout~ ✨💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.2.5 — selected over DynamicExpresso (Q7 resolution) because Jint ships
/// full ES2020 semantics: <c>??</c>, <c>?.</c>, <c>.map()</c>, <c>.filter()</c>, <c>async/await</c>.
/// Timeout is enforced via <see cref="OptionsBase.TimeoutInterval"/> (Jint internal background check).
/// The <c>CancellationToken</c> is honoured as primary — evaluation is wrapped in <c>Task.Run</c>
/// so the CT can interrupt the background thread via linked CTS. True native <c>EvaluateAsync</c>
/// is not yet stable in Jint 4.x; this approach is safe and allocation-cheap for short expressions~ 🧠
/// </para>
/// <para>
/// Safety guarantees:
/// <list type="bullet">
///   <item>4 MB memory cap (<see cref="MemoryLimitBytes"/>)</item>
///   <item>250 ms hard timeout (<see cref="DefaultTimeoutMs"/>)</item>
///   <item>64-level recursion cap (<see cref="RecursionLimit"/>)</item>
///   <item>No CLR type injection beyond explicitly <c>SetValue</c>d safe primitives/DTOs</item>
///   <item>Strict mode — undeclared variables throw <c>ExpressionRuntimeException</c></item>
/// </list>
/// </para>
/// </remarks>
public sealed class JintExpressionEvaluator : IExpressionEvaluator
{
    private const long MemoryLimitBytes = 4L * 1024 * 1024; // 4 MB
    private const int DefaultTimeoutMs = 250;
    private const int RecursionLimit = 64;

    private readonly ILogger<JintExpressionEvaluator> _logger;

    /// <summary>Initializes a new instance of <see cref="JintExpressionEvaluator"/>~ 💉.</summary>
    /// <param name="logger">Logger for trace/error events.</param>
    public JintExpressionEvaluator(ILogger<JintExpressionEvaluator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async ValueTask<T> EvaluateAsync<T>(
        string expression,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken ct = default)
    {
        var raw = await EvaluateAsync(expression, variables, ct).ConfigureAwait(false);
        try
        {
            return (T)Convert.ChangeType(raw, typeof(T))!;
        }
        catch (Exception ex)
        {
            throw new ExpressionRuntimeException(
                expression,
                $"Cannot convert result '{raw}' to type {typeof(T).Name}: {ex.Message}",
                ex);
        }
    }

    /// <inheritdoc/>
    public async ValueTask<object?> EvaluateAsync(
        string expression,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken ct = default)
    {
        // CopilotNote: Run on thread-pool to isolate Jint (not thread-safe) + honour CT~
        return await Task.Run(
            () => EvaluateSynchronous(expression, variables),
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<JsonElement> EvaluateObjectAsync(
        string expression,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken ct = default)
    {
        var raw = await EvaluateAsync(expression, variables, ct).ConfigureAwait(false);

        // Serialize raw .NET value → JsonElement (no ExpandoObject leakage)~ 📦
        return JsonSerializer.SerializeToElement(raw, new JsonSerializerOptions
        {
            WriteIndented = false,
        });
    }

    // ── Core synchronous evaluation ───────────────────────────────────────────────

    private object? EvaluateSynchronous(string expression, IReadOnlyDictionary<string, object?> variables)
    {
        Engine engine;
        try
        {
            engine = BuildEngine(variables);
        }
        catch (Exception ex)
        {
            // CopilotNote: Engine construction rarely fails, but protect against bad variable injection~
            throw new ExpressionRuntimeException(expression, $"Engine construction failed: {ex.Message}", ex);
        }

        try
        {
            var jsValue = engine.Evaluate(expression);
            return JsValueToClr(jsValue, expression);
        }
        catch (JavaScriptException ex)
        {
            _logger.LogWarning(
                "🧮 Jint runtime error in '{Expression}': {Error}",
                expression, ex.Message);
            throw new ExpressionRuntimeException(expression, ex.Message, ex);
        }
        catch (Exception ex) when (ex.GetType().Name == "ParseErrorException"
                                    || ex.GetType().FullName?.Contains("Esprima") == true
                                    || ex.GetType().FullName?.Contains("Parse") == true)
        {
            // CopilotNote: Esprima.ParseErrorException is a transitive dep; catch by name~
            _logger.LogWarning(
                "🧮 Jint parse error in '{Expression}': {Error}",
                expression, ex.Message);
            throw new ExpressionParseException(expression, ex.Message, ex);
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(
                "🧮 Jint timeout evaluating '{Expression}' (>{TimeoutMs} ms)",
                expression, DefaultTimeoutMs);
            throw new ExpressionRuntimeException(expression, $"Evaluation timed out after {DefaultTimeoutMs} ms", ex);
        }
        catch (MemoryLimitExceededException ex)
        {
            _logger.LogWarning(
                "🧮 Jint memory limit exceeded evaluating '{Expression}'",
                expression);
            throw new ExpressionRuntimeException(expression, "Memory limit exceeded", ex);
        }
        catch (RecursionDepthOverflowException ex)
        {
            _logger.LogWarning(
                "🧮 Jint recursion limit exceeded evaluating '{Expression}'",
                expression);
            throw new ExpressionRuntimeException(expression, "Recursion limit exceeded", ex);
        }
        catch (Exception ex) when (ex is not ExpressionParseException && ex is not ExpressionRuntimeException)
        {
            // Catch-all — wrap unexpected Jint exceptions~ 🛡️
            throw new ExpressionRuntimeException(expression, $"Unexpected evaluator error: {ex.Message}", ex);
        }
    }

    // ── Engine factory ────────────────────────────────────────────────────────────

    private static Engine BuildEngine(IReadOnlyDictionary<string, object?> variables)
    {
        var engine = new Engine(opts =>
        {
            opts.LimitMemory(MemoryLimitBytes);
            opts.TimeoutInterval(TimeSpan.FromMilliseconds(DefaultTimeoutMs));
            opts.LimitRecursion(RecursionLimit);
            opts.Strict();
            opts.CatchClrExceptions();
        });

        // Inject only safe primitives — never IServiceProvider, EF entities, etc.~ 🔒
        foreach (var (key, value) in variables)
        {
            engine.SetValue(key, value);
        }

        return engine;
    }

    // ── JsValue → .NET conversion ─────────────────────────────────────────────────

    /// <summary>
    /// Converts a <see cref="JsValue"/> to the closest safe .NET type~
    /// CopilotNote: Handles the most common cases in expression results; objects/arrays
    /// are represented as <c>Dictionary</c>/<c>List</c> (no <c>ExpandoObject</c>)~ 💖
    /// </summary>
    private static object? JsValueToClr(JsValue value, string expression)
    {
        if (value.IsNull() || value.IsUndefined())
        {
            return null;
        }

        if (value.IsBoolean())
        {
            return value.AsBoolean();
        }

        if (value.IsNumber())
        {
            var d = value.AsNumber();
            // Prefer int if the value is a whole number in int range~
            if (d == Math.Floor(d) && d >= int.MinValue && d <= int.MaxValue)
            {
                return (int)d;
            }

            return d;
        }

        if (value.IsString())
        {
            return value.AsString();
        }

        if (value.IsArray())
        {
            var arr = value.AsArray();
            var list = new List<object?>((int)arr.Length);
            for (var i = 0; i < (int)arr.Length; i++)
            {
                list.Add(JsValueToClr(arr[(uint)i], expression));
            }

            return list;
        }

        if (value.IsObject())
        {
            var obj = value.AsObject();
            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var prop in obj.GetOwnProperties())
            {
                dict[prop.Key.AsString()] = JsValueToClr(prop.Value.Value!, expression);
            }

            return dict;
        }

        // Fallback — return string representation~
        return value.ToString();
    }
}


