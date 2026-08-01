// <copyright file="InitialVariables.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Models;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LanguageExt;
using Workflow.Core.Models;

/// <summary>
/// 🌱 Phase 3.5 (V2/V3) — resolves the variable map an execution starts with, by layering every
/// source that can supply a value.
/// </summary>
/// <remarks>
/// <para>
/// Before this existed, <c>WorkflowExecutor</c> seeded the context from the run request's inputs
/// alone: a variable declared on the workflow — with a type, a description and an initial value —
/// had <b>no runtime effect at all</b>, and the persisted variable store was written to but never
/// read, so global variables could never be referenced.
/// </para>
/// <para>
/// Layers, lowest priority first:
/// <list type="number">
/// <item><description><b>Global</b> store values — shared across every workflow.</description></item>
/// <item><description><b>Workflow</b> store values — persisted by previous runs of this workflow.</description></item>
/// <item><description><b>Declared initial values</b> — but a
/// <see cref="VariableSeedMode.SeedOnly"/> declaration only fills a gap, so a value a previous run
/// saved wins over it. <see cref="VariableSeedMode.AlwaysOverride"/> sits above the store.</description></item>
/// <item><description><b>Run inputs</b> — always win, so an ad-hoc run stays easy.</description></item>
/// </list>
/// </para>
/// <para>
/// Pure and actor-free on purpose: the precedence rules are the subtle part, so they're unit
/// testable without spinning up an execution~ ✨.
/// </para>
/// </remarks>
public static class InitialVariables
{
    /// <summary>The resolved map plus anything worth telling the operator about~ 📋.</summary>
    /// <param name="Variables">The starting variable map.</param>
    /// <param name="Warnings">Non-fatal problems (declared-but-unset, type mismatches).</param>
    public sealed record Resolution(HashMap<string, object?> Variables, IReadOnlyList<string> Warnings);

    /// <summary>Layers every source into the map an execution starts with~ 🌱.</summary>
    /// <param name="definition">The workflow definition (supplies the declarations).</param>
    /// <param name="runInputs">Inputs supplied on the run request.</param>
    /// <param name="globalScoped">Values from <c>VariableScope.Global</c>, when a store is present.</param>
    /// <param name="workflowScoped">Values from this workflow's scope, when a store is present.</param>
    /// <returns>The resolved map and any warnings.</returns>
    public static Resolution Resolve(
        WorkflowDefinition definition,
        IReadOnlyDictionary<string, object?>? runInputs,
        IReadOnlyDictionary<string, object?>? globalScoped = null,
        IReadOnlyDictionary<string, object?>? workflowScoped = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        // Case-insensitive to match PropertyBinder's variable lookup, so 'count' and 'Count' can't
        // both end up in the map and shadow each other unpredictably.
        var resolved = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        foreach (var (name, value) in globalScoped ?? Empty)
        {
            resolved[name] = NormalizeStored(value);
        }

        foreach (var (name, value) in workflowScoped ?? Empty)
        {
            resolved[name] = NormalizeStored(value);
        }

        var declarations = definition.Variables.IsEmpty
            ? Array.Empty<VariableDefinition>()
            : definition.Variables.Values.ToArray();

        foreach (var declaration in declarations)
        {
            if (declaration.InitialValue is not { } initial)
            {
                continue;
            }

            // SeedOnly fills a gap; a value a previous run persisted takes precedence over it.
            if (declaration.Seed == VariableSeedMode.SeedOnly && resolved.ContainsKey(declaration.Name))
            {
                continue;
            }

            var value = ConvertJsonElement(initial);
            if (DescribeTypeMismatch(value, declaration.Type) is { } mismatch)
            {
                warnings.Add($"Variable '{declaration.Name}' declares type {declaration.Type} but its initial value {mismatch}. Using it as-is.");
            }

            resolved[declaration.Name] = value;
        }

        foreach (var (name, value) in runInputs ?? Empty)
        {
            resolved[name] = value;
        }

        // Declaring a variable is a contract: it exists for the whole run even when nothing
        // supplied a value, so a reference to it resolves to null rather than failing the node.
        // The gap is worth surfacing though — it's almost always a missing run input.
        foreach (var declaration in declarations)
        {
            if (resolved.ContainsKey(declaration.Name))
            {
                continue;
            }

            resolved[declaration.Name] = null;
            warnings.Add($"Variable '{declaration.Name}' is declared but no value was supplied — it starts as null.");
        }

        return new Resolution(resolved.ToHashMap(), warnings);
    }

    private static IReadOnlyDictionary<string, object?> Empty { get; } = new Dictionary<string, object?>();

    /// <summary>
    /// Converts a value coming out of <c>IVariableStore</c> into a plain CLR value~ 🔄.
    /// </summary>
    /// <remarks>
    /// Every store implementation round-trips values through
    /// <c>JsonSerializer.Deserialize&lt;object?&gt;</c>, which yields a <see cref="JsonElement"/>
    /// for anything non-null. Handing those straight to the binder would make
    /// <c>{{Variable.apiHost}}</c> resolve to a <c>JsonElement</c> rather than a string — so it
    /// would fail type conversion on a typed input, and interpolate with JSON quoting inside a
    /// larger string. Normalising here keeps a stored value indistinguishable from a declared one.
    /// <para>
    /// Run inputs are deliberately <b>not</b> normalised: they already flowed into the variable map
    /// untouched before this change, and the same dictionary is separately used as node inputs by
    /// <c>GatherNodeInputs</c>, so converting them here would be a wider behaviour change than this
    /// work should make. Worth a follow-up.
    /// </para>
    /// </remarks>
    private static object? NormalizeStored(object? value)
        => value is JsonElement element ? ConvertJsonElement(element) : value;

    /// <summary>
    /// Describes how a value contradicts its declared type, or null when it's fine. Deliberately
    /// permissive — this only produces a warning, so it must not cry wolf.
    /// </summary>
    private static string? DescribeTypeMismatch(object? value, PropertyType declared)
    {
        if (value is null)
        {
            return null;
        }

        var ok = declared switch
        {
            PropertyType.String => value is string,
            PropertyType.Int or PropertyType.Long => value is int or long,
            PropertyType.Decimal => value is int or long or double or decimal,
            PropertyType.Boolean => value is bool,
            PropertyType.Object => value is IDictionary<string, object?>,
            PropertyType.Array => value is IList,

            // DateTime/TimeSpan/Guid arrive as strings and are parsed downstream; Connection and
            // Variable are reference markers. None of those can be judged here.
            _ => true,
        };

        return ok ? null : $"is a {DescribeValueKind(value)}";
    }

    private static string DescribeValueKind(object value) => value switch
    {
        string => "string",
        bool => "boolean",
        int or long => "whole number",
        double or decimal or float => "number",
        IDictionary<string, object?> => "object",
        IList => "array",
        _ => value.GetType().Name,
    };

    /// <summary>
    /// Converts a declaration's JSON initial value into the CLR shape the binder and modules
    /// expect~ 🔄.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>NodeExecutor.ConvertJsonElement</c> with one deliberate difference: the number
    /// case casts to <see cref="object"/> so a whole number stays a <see cref="long"/>. Without
    /// that cast the conditional expression's type is <see cref="double"/> (because <c>long</c>
    /// converts implicitly to it), so <c>5</c> would arrive as <c>5.0</c> and an <c>Int</c>
    /// declaration would contradict its own declared type. <c>NodeExecutor</c> still has the
    /// unfixed form — worth a follow-up, but changing node property conversion is a much wider
    /// blast radius than this seeding path.
    /// </remarks>
    private static object? ConvertJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : (object)element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
        _ => element.ToString(),
    };
}
