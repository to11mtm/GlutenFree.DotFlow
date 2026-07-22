// <copyright file="NodePorts.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System.Collections.Generic;
using System.Linq;

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
        };

    /// <summary>Output ports that enter a structural sub-graph (loop body / error boundary)~ 🔁.</summary>
    private static readonly IReadOnlyList<string> StructuralPortNames = new[] { "loopBody", "try", "catch", "finally" };

    /// <summary>Returns whether an edge leaving this port enters a structural sub-graph~ 🔁.</summary>
    /// <param name="sourcePortName">The edge's source port name.</param>
    /// <returns>True for loop-body / try / catch / finally routes.</returns>
    public static bool IsStructuralPort(string sourcePortName)
        => StructuralPortNames.Contains(sourcePortName);

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
        var outputs = node.Schema is { Outputs: { Count: > 0 } o } ? o.Select(p => p.Name).ToList()
            : DynamicOutputs.TryGetValue(node.ModuleId, out var dyn) ? dyn.ToList()
            : DefaultOutputs.ToList();

        // UX-R1: FanIn's 'meta' selection controls whether count/done render as ports.
        if (node.ModuleId == "builtin.fanin" && FanInMetaMode(node) is "embedded" or "hidden")
        {
            outputs = outputs.Where(p => p is not ("count" or "done")).ToList();
        }

        return outputs;
    }

    private static string FanInMetaMode(DesignerNode node)
        => node.Properties.TryGetValue("meta", out var v)
           && v.ValueKind == System.Text.Json.JsonValueKind.String
           && v.GetString() is { Length: > 0 } s
            ? s.ToLowerInvariant()
            : "separate";

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
