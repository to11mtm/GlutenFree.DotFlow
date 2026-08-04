// <copyright file="NodePorts.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// 🔌 Phase 3.3.a.3 — Resolves a node's input/output port names from its schema, with sensible
/// defaults for unknown modules. Shared by <c>NodeView</c> (rendering) and <c>EdgeLayer</c> (anchor
/// math) so the two never diverge~ ✨.
/// </summary>
public static class NodePorts
{
    private static readonly IReadOnlyList<string> DefaultInputs = new[] { "input" };
    private static readonly IReadOnlyList<string> DefaultOutputs = new[] { "output" };

    /// <summary>
    /// Modules whose ports are dynamic (partial/empty schema) — the designer surfaces their
    /// conventional routing ports so bodies can be wired visually (UX-F5.4)~ 🛡️.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DynamicOutputs =
        new Dictionary<string, IReadOnlyList<string>>(System.StringComparer.Ordinal)
        {
            ["builtin.trycatch"] = new[] { "try", "catch", "finally", "done" },
            ["builtin.database.transaction"] = new[] { "transactionBody", "committed", "rolledBack" },
        };

    /// <summary>
    /// Extra designer-surfaced input ports for modules with dynamic schemas: trycatch declares
    /// only <c>rethrow</c>/<c>catchTypes</c>, so an <c>input</c> activation port is added for
    /// wiring a predecessor (the server skips port-name validation for this module)~ 🔌.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DynamicExtraInputs =
        new Dictionary<string, IReadOnlyList<string>>(System.StringComparer.Ordinal)
        {
            ["builtin.trycatch"] = new[] { "input" },
            ["builtin.database.transaction"] = new[] { "input" },
        };

    /// <summary>Output ports that enter a structural sub-graph (loop body / error boundary / transaction)~ 🔁.</summary>
    private static readonly IReadOnlyList<string> StructuralPortNames =
        new[] { "loopBody", "try", "catch", "finally", "transactionBody" };

    /// <summary>Returns whether an edge leaving this port enters a structural sub-graph~ 🔁.</summary>
    /// <param name="sourcePortName">The edge's source port name.</param>
    /// <returns>True for loop-body / try / catch / finally / transaction-body routes.</returns>
    public static bool IsStructuralPort(string sourcePortName)
        => StructuralPortNames.Contains(sourcePortName);

    /// <summary>
    /// Module-aware structural check: also treats fanout's <c>branch</c> and parallel's branch
    /// ports as structural (their downstream closure is a per-item / per-branch sub-graph), while
    /// a plain module with a port that merely *happens* to be named <c>branch</c> is not~ 🌟.
    /// </summary>
    /// <param name="sourceNode">The edge's source node (null when dangling).</param>
    /// <param name="sourcePortName">The edge's source port name.</param>
    /// <returns>True when the edge enters a structural sub-graph.</returns>
    public static bool IsStructuralEdge(DesignerNode? sourceNode, string sourcePortName)
        => IsStructuralPort(sourcePortName)
           || (sourceNode?.ModuleId == "builtin.fanout" && sourcePortName == "branch")
           || (sourceNode?.ModuleId == "builtin.parallel" && sourcePortName != "done");

    /// <summary>Gets the input port names for a node~ 🔌.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The input port names.</returns>
    public static IReadOnlyList<string> Inputs(DesignerNode node)
    {
        var declared = node.Schema is { Inputs: { Count: > 0 } i } ? i.Select(p => p.Name).ToList() : null;
        if (DynamicExtraInputs.TryGetValue(node.ModuleId, out var extra))
        {
            var merged = new List<string>(extra);
            if (declared is not null)
            {
                merged.AddRange(declared.Where(p => !extra.Contains(p)));
            }

            return merged;
        }

        return declared ?? DefaultInputs;
    }

    /// <summary>Gets the output port names for a node~ 🔌.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The output port names.</returns>
    public static IReadOnlyList<string> Outputs(DesignerNode node)
    {
        var outputs = PropertyDerivedOutputs(node)?.ToList()
            ?? (node.Schema is { Outputs: { Count: > 0 } o } ? o.Select(p => p.Name).ToList()
            : DynamicOutputs.TryGetValue(node.ModuleId, out var dyn) ? dyn.ToList()
            : DefaultOutputs.ToList());

        // UX-R1: FanIn's 'meta' selection controls whether count/done render as ports.
        if (node.ModuleId == "builtin.fanin" && FanInMetaMode(node) is "embedded" or "hidden")
        {
            outputs = outputs.Where(p => p is not ("count" or "done")).ToList();
        }

        // 🎚️ Universal merged-output mode collapses everything to the single 'output' port.
        if (OutputShapingUx.IsEligible(node) && OutputShapingUx.IsMerged(node))
        {
            return new[] { OutputShapingUx.MergedPortName };
        }

        return outputs;
    }

    private static string FanInMetaMode(DesignerNode node)
        => node.Properties.TryGetValue("meta", out var v)
           && v.ValueKind == System.Text.Json.JsonValueKind.String
           && v.GetString() is { Length: > 0 } s
            ? s.ToLowerInvariant()
            : "separate";

    // ── Property-derived dynamic output ports (P2) ──────────────────────────────────────
    // Switch/partition/split/parallel declare EMPTY schema outputs (the engine skips port-name
    // validation for them); their real legs live in a property. Deriving ports from that property
    // is what makes those legs wireable — and visible — in the designer~ 🔌✨

    /// <summary>
    /// Derives output ports from a node's routing properties for modules with dynamic legs, or
    /// null when the module isn't one of them / the property doesn't parse (schema fallback)~ 🧮.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The derived output ports, or null.</returns>
    public static IReadOnlyList<string>? PropertyDerivedOutputs(DesignerNode node)
        => node.ModuleId switch
        {
            "builtin.switch" => AppendUnique(
                PortsFromRuleTable(node, "cases"), StringProperty(node, "defaultPort")),
            "builtin.partition" => PortsFromRuleTable(node, "rules") is { } legs
                ? AppendUnique(AppendUnique(legs, StringProperty(node, "defaultPort")), "counts", "total")
                : null,
            "builtin.split" => StringArrayProperty(node, "keys") is { } keys
                ? AppendUnique(keys, StringProperty(node, "restPort"))
                : null,
            "builtin.parallel" => AppendUnique(ParallelBranchPorts(node), "done"),
            _ => null,
        };

    /// <summary>Reads the distinct <c>port</c> names, in order, from a Switch-style rule table property~ 📋.</summary>
    private static List<string>? PortsFromRuleTable(DesignerNode node, string propertyName)
    {
        if (!TryGetJsonProperty(node, propertyName, out var el) || el.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var ports = new List<string>();
        foreach (var entry in el.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("port", out var p)
                && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } name
                && !ports.Contains(name))
            {
                ports.Add(name);
            }
        }

        return ports.Count > 0 ? ports : null;
    }

    /// <summary>Reads a JSON array-of-strings property (e.g. Split's <c>keys</c>)~ 🔑.</summary>
    private static List<string>? StringArrayProperty(DesignerNode node, string propertyName)
    {
        if (!TryGetJsonProperty(node, propertyName, out var el) || el.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = new List<string>();
        foreach (var entry in el.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String
                && entry.GetString() is { Length: > 0 } s
                && !values.Contains(s))
            {
                values.Add(s);
            }
        }

        return values.Count > 0 ? values : null;
    }

    /// <summary>Parallel's legs: the <c>branches</c> name list, or <c>branch1..branchN</c> from <c>branchCount</c> (default 2)~ 🌐.</summary>
    private static List<string> ParallelBranchPorts(DesignerNode node)
    {
        if (StringArrayProperty(node, "branches") is { } named)
        {
            return named;
        }

        var count = 2;
        if (node.Properties.TryGetValue("branchCount", out var bc))
        {
            if (bc.ValueKind == JsonValueKind.Number && bc.TryGetInt32(out var n) && n > 0)
            {
                count = n;
            }
            else if (bc.ValueKind == JsonValueKind.String && int.TryParse(bc.GetString(), out var ns) && ns > 0)
            {
                count = ns;
            }
        }

        return Enumerable.Range(1, count).Select(i => $"branch{i}").ToList();
    }

    /// <summary>Reads a non-blank string property, or null~ 💬.</summary>
    private static string? StringProperty(DesignerNode node, string propertyName)
        => node.Properties.TryGetValue(propertyName, out var v)
           && v.ValueKind == JsonValueKind.String
           && v.GetString() is { Length: > 0 } s && !string.IsNullOrWhiteSpace(s)
            ? s.Trim()
            : null;

    /// <summary>Appends values to a port list, skipping blanks and duplicates. Null list stays null unless a value forces it~ ➕.</summary>
    private static List<string>? AppendUnique(List<string>? ports, params string?[] extra)
    {
        foreach (var e in extra)
        {
            if (string.IsNullOrWhiteSpace(e))
            {
                continue;
            }

            ports ??= new List<string>();
            if (!ports.Contains(e))
            {
                ports.Add(e);
            }
        }

        return ports;
    }

    /// <summary>
    /// Gets a property as a JSON element, unwrapping the JSON-editor convention of storing the
    /// value as a JSON <em>string</em> (parses it) as well as native arrays/objects~ 🎁.
    /// </summary>
    private static bool TryGetJsonProperty(DesignerNode node, string propertyName, out JsonElement element)
    {
        element = default;
        if (!node.Properties.TryGetValue(propertyName, out var raw))
        {
            return false;
        }

        if (raw.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
        {
            element = raw;
            return true;
        }

        if (raw.ValueKind == JsonValueKind.String && raw.GetString() is { Length: > 0 } s)
        {
            try
            {
                using var doc = JsonDocument.Parse(s);
                element = doc.RootElement.Clone();
                return true;
            }
            catch (System.Text.Json.JsonException)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>Computes the canvas-space anchor point for a node port~ 📍.</summary>
    /// <param name="node">The node.</param>
    /// <param name="portName">The port name.</param>
    /// <param name="isOutput">True for an output port (right edge), false for input (left edge).</param>
    /// <returns>The anchor point.</returns>
    public static Point Anchor(DesignerNode node, string portName, bool isOutput)
    {
        var inputs = Inputs(node);
        var outputs = Outputs(node);
        var bounds = CanvasGeometry.NodeBounds(node.X, node.Y, inputs.Count, outputs.Count);

        var list = isOutput ? outputs : inputs;
        var index = list.ToList().IndexOf(portName);
        if (index < 0)
        {
            index = 0;
        }

        return CanvasGeometry.PortAnchor(bounds, index, isInput: !isOutput);
    }
}
