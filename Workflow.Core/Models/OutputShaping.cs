// <copyright file="OutputShaping.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Core.Models;

using System;
using System.Collections.Generic;

/// <summary>
/// 🎚️ UX — the universal "merged output" option: a reserved per-node property
/// (<see cref="PropertyName"/>) that, when set to <see cref="Merged"/>, makes the engine emit a
/// single <see cref="MergedPortName"/> port whose value is an object of all the module's outputs.
/// Handled centrally by the engine — modules never see it~ ✨.
/// </summary>
public static class OutputShaping
{
    /// <summary>The reserved node property name~ 🔑.</summary>
    public const string PropertyName = "outputMode";

    /// <summary>Default mode — outputs surface as their declared ports~ 🔌.</summary>
    public const string Ports = "ports";

    /// <summary>Merged mode — one <see cref="MergedPortName"/> port carrying all outputs~ 📦.</summary>
    public const string Merged = "merged";

    /// <summary>The single output port name used in merged mode~ 🏷️.</summary>
    public const string MergedPortName = "output";

    /// <summary>Returns whether a raw property value selects merged mode~ 🎚️.</summary>
    /// <param name="value">The property value (string expected).</param>
    /// <returns>True for "merged" (case-insensitive).</returns>
    public static bool IsMerged(object? value)
        => value is string s && string.Equals(s, Merged, StringComparison.OrdinalIgnoreCase);

    /// <summary>Wraps a module's outputs into the single merged port~ 📦.</summary>
    /// <param name="outputs">The module's raw outputs.</param>
    /// <returns>A dictionary with one <see cref="MergedPortName"/> entry containing them all.</returns>
    public static Dictionary<string, object?> Merge(IReadOnlyDictionary<string, object?> outputs)
        => new(StringComparer.Ordinal)
        {
            [MergedPortName] = new Dictionary<string, object?>(outputs, StringComparer.Ordinal),
        };
}
