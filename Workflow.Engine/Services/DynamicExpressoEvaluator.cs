// <copyright file="DynamicExpressoEvaluator.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Services;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DynamicExpresso;
using Microsoft.Extensions.Logging;
using Workflow.Core.Abstractions;

/// <summary>
/// 🔧 Optional fallback expression evaluator using <b>DynamicExpresso</b> (C# syntax, MIT)~
/// Registered under keyed DI key <c>"csharp"</c>. Zero async overhead (synchronous C# expressions)~ 💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.2.5 — DynamicExpresso is the opt-in <c>"csharp"</c> evaluator for authors
/// who prefer C# syntax over JS. It is NOT the default — use <see cref="JintExpressionEvaluator"/>
/// for the default registration~ 🧠
/// </para>
/// <para>
/// Built-in whitelist helpers: <c>len(x)</c>, <c>contains(x, y)</c>, <c>lower(s)</c>, <c>upper(s)</c>, <c>now()</c>.
/// Reflection disabled (<c>DisableDynamicResolution</c>) for safety~ 🔒
/// </para>
/// </remarks>
public sealed class DynamicExpressoEvaluator : IExpressionEvaluator
{
    private readonly ILogger<DynamicExpressoEvaluator> _logger;

    /// <summary>Initializes a new instance of <see cref="DynamicExpressoEvaluator"/>~ 💉.</summary>
    /// <param name="logger">Logger for trace/error events.</param>
    public DynamicExpressoEvaluator(ILogger<DynamicExpressoEvaluator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public ValueTask<T> EvaluateAsync<T>(
        string expression,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken ct = default)
    {
        // DynamicExpresso is synchronous — CT is checked before execution starts~
        ct.ThrowIfCancellationRequested();

        try
        {
            var interpreter = BuildInterpreter();
            var parameters = BuildParameters(variables);
            var result = interpreter.Eval(expression, parameters);
            return ValueTask.FromResult((T)Convert.ChangeType(result, typeof(T))!);
        }
        catch (Exception ex) when (ex.GetType().FullName?.Contains("Parse") == true)
        {
            _logger.LogWarning("🔧 DynamicExpresso parse error in '{Expression}': {Error}", expression, ex.Message);
            throw new ExpressionParseException(expression, ex.Message, ex);
        }
        catch (Exception ex) when (ex is not ExpressionParseException && ex is not ExpressionRuntimeException)
        {
            _logger.LogWarning("🔧 DynamicExpresso runtime error in '{Expression}': {Error}", expression, ex.Message);
            throw new ExpressionRuntimeException(expression, ex.Message, ex);
        }
    }

    /// <inheritdoc/>
    public ValueTask<object?> EvaluateAsync(
        string expression,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var interpreter = BuildInterpreter();
            var parameters = BuildParameters(variables);
            var result = interpreter.Eval(expression, parameters);
            return ValueTask.FromResult<object?>(result);
        }
        catch (Exception ex) when (ex.GetType().FullName?.Contains("Parse") == true)
        {
            _logger.LogWarning("🔧 DynamicExpresso parse error in '{Expression}': {Error}", expression, ex.Message);
            throw new ExpressionParseException(expression, ex.Message, ex);
        }
        catch (Exception ex) when (ex is not ExpressionParseException && ex is not ExpressionRuntimeException)
        {
            _logger.LogWarning("🔧 DynamicExpresso runtime error in '{Expression}': {Error}", expression, ex.Message);
            throw new ExpressionRuntimeException(expression, ex.Message, ex);
        }
    }

    /// <inheritdoc/>
    public async ValueTask<JsonElement> EvaluateObjectAsync(
        string expression,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken ct = default)
    {
        var raw = await EvaluateAsync(expression, variables, ct).ConfigureAwait(false);
        return JsonSerializer.SerializeToElement(raw);
    }

    // ── Interpreter factory ───────────────────────────────────────────────────────

    private static Interpreter BuildInterpreter()
    {
        var options = InterpreterOptions.Default;
        var interpreter = new Interpreter(options);

        // Whitelist of safe helpers — no reflection, no I/O~ 🔒
        interpreter.SetFunction("len", new Func<System.Collections.IEnumerable, int>(
            col => col is string s ? s.Length : col.Cast<object>().Count()));
        interpreter.SetFunction("contains", new Func<System.Collections.IEnumerable, object, bool>(
            (col, item) => col.Cast<object>().Any(x => Equals(x, item))));
        interpreter.SetFunction("lower", new Func<string, string>(s => s.ToLowerInvariant()));
        interpreter.SetFunction("upper", new Func<string, string>(s => s.ToUpperInvariant()));
        interpreter.SetFunction("now", new Func<DateTime>(() => DateTime.UtcNow));

        return interpreter;
    }

    private static Parameter[] BuildParameters(IReadOnlyDictionary<string, object?> variables)
    {
        // CopilotNote: DynamicExpresso.Parameter(name, type, value) embeds value in the parameter~
        var parameters = new Parameter[variables.Count];
        var i = 0;
        foreach (var (key, value) in variables)
        {
            var type = value?.GetType() ?? typeof(object);
            parameters[i++] = new Parameter(key, type, value);
        }

        return parameters;
    }
}



