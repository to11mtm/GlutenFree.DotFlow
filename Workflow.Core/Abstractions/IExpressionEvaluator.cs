// <copyright file="IExpressionEvaluator.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Core.Abstractions;

using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 🧮 Contract for a safe, sandboxed expression evaluator used by control-flow modules~
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.2.1 defines this interface so <c>builtin.condition</c> and
/// <c>builtin.switch</c> can resolve string expressions at runtime (e.g. <c>"x &gt; 5"</c>).
/// The default implementation (Jint/JS) ships in Phase 2.2.5~ 🌸
/// </para>
/// <para>
/// Design contract:
/// <list type="bullet">
///   <item>Expressions are deterministic — no I/O, no reflection, no side-effects.</item>
///   <item>Variables are injected by the caller; the evaluator cannot access the process environment.</item>
///   <item>Evaluation is <c>async</c> to support JS <c>async/await</c> and native CT propagation.</item>
/// </list>
/// </para>
/// </remarks>
public interface IExpressionEvaluator
{
    /// <summary>
    /// Evaluates the given expression and returns the result cast to <typeparamref name="T"/>~ 🎯
    /// </summary>
    /// <typeparam name="T">The expected return type (e.g. <c>bool</c>, <c>int</c>, <c>string</c>).</typeparam>
    /// <param name="expression">The expression string to evaluate (JS/ES2020 for Jint; C# for DynamicExpresso).</param>
    /// <param name="variables">Read-only variable scope to inject into the evaluator. Only safe .NET primitives + DTOs.</param>
    /// <param name="ct">Cancellation token — natively respected by the evaluator (primary timeout mechanism).</param>
    /// <returns>The evaluated value cast to <typeparamref name="T"/>.</returns>
    /// <exception cref="ExpressionParseException">Thrown for syntax / parse-time errors.</exception>
    /// <exception cref="ExpressionRuntimeException">Thrown for runtime / timeout errors.</exception>
    ValueTask<T> EvaluateAsync<T>(
        string expression,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken ct = default);

    /// <summary>
    /// Evaluates the given expression and returns the raw result as an untyped object~ 🔬
    /// </summary>
    /// <param name="expression">The expression string.</param>
    /// <param name="variables">Variable scope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The evaluated value as <c>object?</c>.</returns>
    /// <exception cref="ExpressionParseException">Thrown for syntax / parse-time errors.</exception>
    /// <exception cref="ExpressionRuntimeException">Thrown for runtime / timeout errors.</exception>
    ValueTask<object?> EvaluateAsync(
        string expression,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken ct = default);

    /// <summary>
    /// Evaluates the given expression and returns a structured <see cref="JsonElement"/>
    /// for object/array results (avoids <c>ExpandoObject</c> leakage)~ 📦
    /// </summary>
    /// <param name="expression">The expression string.</param>
    /// <param name="variables">Variable scope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="JsonElement"/> representing the evaluated value.</returns>
    /// <exception cref="ExpressionParseException">Thrown for syntax / parse-time errors.</exception>
    /// <exception cref="ExpressionRuntimeException">Thrown for runtime / timeout errors.</exception>
    ValueTask<JsonElement> EvaluateObjectAsync(
        string expression,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken ct = default);
}

/// <summary>
/// 💥 Thrown when the expression cannot be parsed (syntax error, unbalanced parens, etc.)~ ✨
/// </summary>
/// <param name="expression">The expression that failed to parse.</param>
/// <param name="reason">Human-readable reason for the failure.</param>
/// <param name="innerException">Optional underlying parse exception.</param>
public class ExpressionParseException(
    string expression,
    string reason,
    System.Exception? innerException = null)
    : System.Exception($"Expression parse error in '{expression}': {reason}", innerException)
{
    /// <summary>Gets the expression that failed to parse.</summary>
    public string Expression { get; } = expression;

    /// <summary>Gets the human-readable reason for the failure.</summary>
    public string Reason { get; } = reason;
}

/// <summary>
/// ⏰ Thrown when the expression fails or times out during runtime evaluation~ 💔
/// </summary>
/// <param name="expression">The expression that failed at runtime.</param>
/// <param name="reason">Human-readable reason for the failure.</param>
/// <param name="innerException">Optional underlying runtime exception.</param>
public class ExpressionRuntimeException(
    string expression,
    string reason,
    System.Exception? innerException = null)
    : System.Exception($"Expression runtime error in '{expression}': {reason}", innerException)
{
    /// <summary>Gets the expression that failed at runtime.</summary>
    public string Expression { get; } = expression;

    /// <summary>Gets the human-readable reason for the failure.</summary>
    public string Reason { get; } = reason;
}

