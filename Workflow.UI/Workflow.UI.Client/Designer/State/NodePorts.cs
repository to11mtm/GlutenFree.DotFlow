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
    /// Modules whose output ports are dynamic (empty schema) — the designer surfaces their
    /// conventional routing ports so bodies can be wired visually (UX-F5.4)~ 🛡️.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DynamicOutputs =
        new Dictionary<string, IReadOnlyList<string>>(System.StringComparer.Ordinal)
        {
            ["builtin.trycatch"] = new[] { "try", "catch", "finally", "done" },
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
        => node.Schema is { Inputs: { Count: > 0 } i } ? i.Select(p => p.Name).ToList() : DefaultInputs;

    /// <summary>Gets the output port names for a node~ 🔌.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The output port names.</returns>
    public static IReadOnlyList<string> Outputs(DesignerNode node)
        => node.Schema is { Outputs: { Count: > 0 } o } ? o.Select(p => p.Name).ToList()
            : DynamicOutputs.TryGetValue(node.ModuleId, out var dyn) ? dyn
            : DefaultOutputs;

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
