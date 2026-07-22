// <copyright file="OutputShapingUx.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// 🎚️ UX — designer-side rules for the universal "merged output" option (mirrors the engine's
/// reserved <c>outputMode</c> property; constants duplicated to keep the UI contracts-only, D2).
/// Framework-free~ ✨.
/// </summary>
public static class OutputShapingUx
{
    /// <summary>The reserved node property name~ 🔑.</summary>
    public const string PropertyName = "outputMode";

    /// <summary>Default mode — declared output ports~ 🔌.</summary>
    public const string Ports = "ports";

    /// <summary>Merged mode — one <see cref="MergedPortName"/> port with all outputs~ 📦.</summary>
    public const string Merged = "merged";

    /// <summary>The single output port name in merged mode~ 🏷️.</summary>
    public const string MergedPortName = "output";

    /// <summary>
    /// Control-flow / structural modules whose ports are routes, not data — never eligible.
    /// FanIn is excluded too: it has its own richer <c>meta</c> option~ 🚫.
    /// </summary>
    private static readonly HashSet<string> ExcludedModules = new(StringComparer.Ordinal)
    {
        "builtin.condition", "builtin.switch", "builtin.fanout", "builtin.fanin",
        "builtin.parallel", "builtin.loop.foreach", "builtin.loop.while",
        "builtin.break", "builtin.continue", "builtin.throw", "builtin.trycatch",
    };

    /// <summary>
    /// Whether the node can offer the merged-output selector: a data module with at least two
    /// declared output ports~ 🎚️.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>True when eligible.</returns>
    public static bool IsEligible(DesignerNode node)
        => !ExcludedModules.Contains(node.ModuleId)
           && node.Schema is { Outputs.Count: >= 2 };

    /// <summary>Whether the node currently selects merged mode~ 📦.</summary>
    /// <param name="node">The node.</param>
    /// <returns>True when <c>outputMode</c> is "merged".</returns>
    public static bool IsMerged(DesignerNode node)
        => node.Properties.TryGetValue(PropertyName, out var v)
           && v.ValueKind == JsonValueKind.String
           && string.Equals(v.GetString(), Merged, StringComparison.OrdinalIgnoreCase);
}
