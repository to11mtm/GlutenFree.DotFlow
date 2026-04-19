// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

namespace Workflow.Modules.Binding;

using System;
using System.Collections.Generic;
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
    /// <returns>
    /// A <see cref="PropertyBindingResult"/> containing bound values or accumulated errors.
    /// </returns>
    public PropertyBindingResult BindProperties(
        IReadOnlyDictionary<string, object?> rawValues,
        Arr<PortDefinition> schema,
        PropertyBindingContext context);
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
/// <remarks>
/// CopilotNote: Variables come from WorkflowDefinition.Variables + any runtime mutations.
/// NodeOutputs come from WorkflowExecutor._nodeOutputs. Both are read-only snapshots
/// at binding time — the binder doesn't mutate anything! Clean and pure. 💖.
/// </remarks>
public record PropertyBindingContext(
    IReadOnlyDictionary<string, object?> Variables,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> NodeOutputs,
    IServiceProvider? ServiceProvider = null)
{
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
