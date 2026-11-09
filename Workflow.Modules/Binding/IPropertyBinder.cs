// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

namespace Workflow.Modules.Binding;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Workflow.Core.Models;

/// <summary>
/// 🔗 Binds raw property/input values to their final resolved forms.
/// Handles variable reference resolution, type conversion, default application,
/// and schema validation — the full binding pipeline! UwU ✨.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: This is the core binding contract for Phase 1.4.4. The binder
/// sits between raw dictionary values (from NodeDefinition or connections) and
/// the strongly-typed values that modules expect. It resolves <c>{{Variable.Name}}</c>
/// and <c>{{NodeId.OutputName}}</c> patterns, converts types, applies defaults,
/// and validates everything against the port/property schema. 💖.
/// </para>
/// <para>
/// Designed to be usable both in the engine (NodeExecutor) and in the UI
/// (workflow designer property panels) — it lives in Workflow.Modules
/// to keep it close to the module system but separate from the Akka engine.
/// </para>
/// </remarks>
public interface IPropertyBinder
{
    /// <summary>
    /// Binds raw property/input values against a port schema, resolving references,
    /// converting types, applying defaults, and validating. 🎯.
    /// </summary>
    /// <param name="rawValues">
    /// The unprocessed input/property values (may contain <c>{{...}}</c> references
    /// or values that need type conversion).
    /// </param>
    /// <param name="schema">
    /// The port definitions to bind against. Defines expected names, types,
    /// required-ness, and default values.
    /// </param>
    /// <param name="context">
    /// The binding context providing workflow variables and predecessor node outputs
    /// for reference resolution.
    /// </param>
    /// <param name="cancellationToken">Cancels expression evaluation (the binding then throws).</param>
    /// <returns>
    /// A <see cref="PropertyBindingResult"/> containing bound values or accumulated errors.
    /// </returns>
    public ValueTask<PropertyBindingResult> BindPropertiesAsync(
        IReadOnlyDictionary<string, object?> rawValues,
        Arr<PortDefinition> schema,
        PropertyBindingContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 🔗 Phase 3.5 (V4) — resolves <c>{{…}}</c> references in a node's **properties**.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Properties are the designer's entire editing surface, yet until this existed the engine only
    /// ever expanded references in module <em>inputs</em> — so a token a user inserted through the
    /// <c>{{x}}</c> picker or ƒx builder reached the module as the literal text
    /// <c>"{{Variable.host}}"</c>.
    /// </para>
    /// <para>
    /// Only properties whose schema sets <see cref="ModulePropertyDefinition.SupportsTemplates"/>
    /// are resolved; everything else — SQL text, script bodies, connection strings, and any value
    /// that isn't a string — passes through byte-for-byte. Unlike
    /// <see cref="BindPropertiesAsync"/>
    /// this deliberately performs **no** type conversion, default application or required-ness
    /// checking: modules parse their own properties, and changing that is a far wider concern.
    /// </para>
    /// </remarks>
    /// <param name="rawValues">The node's raw property values.</param>
    /// <param name="schema">The module's property definitions.</param>
    /// <param name="context">Variables and upstream node outputs for resolution.</param>
    /// <param name="cancellationToken">Cancels expression evaluation (the binding then throws).</param>
    /// <returns>The resolved values, or accumulated errors when a reference can't be resolved.</returns>
    public ValueTask<PropertyBindingResult> BindModulePropertiesAsync(
        IReadOnlyDictionary<string, object?> rawValues,
        Arr<ModulePropertyDefinition> schema,
        PropertyBindingContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 🔀 Stage 7.2 — evaluates a connection's condition: an expression in the <c>{{…}}</c> language
    /// (braces optional) where <c>value</c> is the value travelling on the edge, and
    /// <c>Variable.x</c> / <c>nodeId.port</c> resolve as in templates. The result must be a boolean.
    /// </summary>
    /// <param name="expression">The condition.</param>
    /// <param name="context">Variables and upstream outputs.</param>
    /// <param name="value">The edge's value (the source port's output).</param>
    /// <param name="cancellationToken">Cancels the evaluation (it then throws).</param>
    /// <returns>True/false, or why it couldn't be evaluated.</returns>
    public ValueTask<ConditionResult> EvaluateConditionAsync(
        string expression,
        PropertyBindingContext context,
        object? value,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(ConditionResult.Error("this binder can't evaluate edge conditions (no expression evaluator)"));
}

/// <summary>🔀 Stage 7.2 — the outcome of <see cref="IPropertyBinder.EvaluateConditionAsync"/>.</summary>
/// <param name="Value">The result (meaningful when <paramref name="Failure"/> is null).</param>
/// <param name="Failure">Why the condition couldn't be evaluated, or null.</param>
public sealed record ConditionResult(bool Value, string? Failure)
{
    /// <summary>A true/false result.</summary>
    /// <param name="value">The result.</param>
    /// <returns>The outcome.</returns>
    public static ConditionResult Of(bool value) => new(value, null);

    /// <summary>A failure.</summary>
    /// <param name="failure">Why.</param>
    /// <returns>The outcome.</returns>
    public static ConditionResult Error(string failure) => new(false, failure);
}

/// <summary>
/// 📦 Context provided to the property binder for resolving references.
/// Contains workflow variables and predecessor node outputs. UwU ✨.
/// </summary>
/// <param name="Variables">
/// Workflow-level variables available for <c>{{Variable.Name}}</c> resolution.
/// </param>
/// <param name="NodeOutputs">
/// Predecessor node outputs available for <c>{{NodeId.OutputName}}</c> resolution.
/// Outer key = nodeId, inner key = output port name.
/// </param>
/// <param name="ServiceProvider">
/// Optional service provider for advanced binding scenarios (e.g., custom converters).
/// </param>
/// <param name="SelfInputs">
/// 🔌 The executing node's own gathered inputs (keyed by input port name), available for
/// <c>{{input}}</c> / <c>{{input.path}}</c> resolution. Null when the binder runs outside a node
/// execution (e.g. standalone binding scenarios) — the <c>input</c> root then stays inert.
/// </param>
/// <remarks>
/// CopilotNote: Variables come from WorkflowDefinition.Variables + any runtime mutations.
/// NodeOutputs come from WorkflowExecutor._nodeOutputs. Both are read-only snapshots
/// at binding time — the binder doesn't mutate anything! Clean and pure. 💖.
/// </remarks>
public record PropertyBindingContext(
    IReadOnlyDictionary<string, object?> Variables,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> NodeOutputs,
    IServiceProvider? ServiceProvider = null,
    IReadOnlyDictionary<string, object?>? SelfInputs = null)
{
    /// <summary>
    /// §10.1 — explains why a <c>{{nodeId.port}}</c> node isn't in <see cref="NodeOutputs"/> (not
    /// upstream, or upstream but didn't run). Null = the generic message.
    /// </summary>
    public Func<string, string?>? ExplainMissingNode { get; init; }

    /// <summary>
    /// Creates an empty context with no variables or node outputs.
    /// Useful for simple binding scenarios and testing. 🧪.
    /// </summary>
    public static PropertyBindingContext Empty => new(
        new Dictionary<string, object?>(),
        new Dictionary<string, IReadOnlyDictionary<string, object?>>());
}

/// <summary>
/// 🎯 Result of property binding — either successfully bound values or accumulated errors.
/// </summary>
/// <param name="Success">Whether all bindings resolved without errors.</param>
/// <param name="BoundValues">
/// The final resolved and type-converted values, keyed by port name.
/// Only meaningful when <see cref="Success"/> is true.
/// </param>
/// <param name="Errors">
/// Accumulated binding errors (missing required values, type mismatches, unresolved references).
/// Empty when <see cref="Success"/> is true.
/// </param>
/// <remarks>
/// CopilotNote: We accumulate ALL errors rather than short-circuiting on the first one.
/// This gives the caller (NodeExecutor or UI) a complete picture of what needs fixing! 💖.
/// </remarks>
public record PropertyBindingResult(
    bool Success,
    IReadOnlyDictionary<string, object?> BoundValues,
    Arr<string> Errors)
{
    /// <summary>
    /// Creates a successful binding result with the resolved values. ✨.
    /// </summary>
    /// <param name="boundValues">The fully resolved property values.</param>
    /// <returns>A successful <see cref="PropertyBindingResult"/>.</returns>
    public static PropertyBindingResult Ok(IReadOnlyDictionary<string, object?> boundValues)
        => new(true, boundValues, Arr<string>.Empty);

    /// <summary>
    /// Creates a failed binding result with accumulated errors. 💥.
    /// </summary>
    /// <param name="errors">The binding errors that occurred.</param>
    /// <returns>A failed <see cref="PropertyBindingResult"/>.</returns>
    public static PropertyBindingResult Fail(Arr<string> errors)
        => new(false, new Dictionary<string, object?>(), errors);

    /// <summary>
    /// Creates a failed binding result from a params array of error strings. 💥.
    /// </summary>
    /// <param name="errors">The binding errors.</param>
    /// <returns>A failed <see cref="PropertyBindingResult"/>.</returns>
    public static PropertyBindingResult Fail(params string[] errors)
        => new(false, new Dictionary<string, object?>(), errors.ToArr());
}
