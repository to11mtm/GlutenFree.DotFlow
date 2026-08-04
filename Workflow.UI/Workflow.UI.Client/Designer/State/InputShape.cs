// <copyright file="InputShape.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 🔎 Input-shape hinting (T1) — derives, from schemas and the graph alone, what data is
/// addressable from a node being configured: each upstream node's output ports, the sub-keys of a
/// <b>merged</b> node's single <c>output</c> object (they are exactly its pre-collapse schema
/// ports), a named FanIn's computed result keys, and the node's own <c>{{input}}</c> value.
/// No runtime data involved — the shape is deterministic (F2). Framework-free (D2)~ ✨.
/// </summary>
public static class InputShape
{
    /// <summary>One addressable key: a token plus what it is~ 🎫.</summary>
    /// <param name="Token">The literal <c>{{…}}</c> token.</param>
    /// <param name="Label">The display label (e.g. <c>output.statusCode</c>).</param>
    /// <param name="DataType">The schema data type, when known.</param>
    /// <param name="Description">The schema description, when present.</param>
    public sealed record Key(string Token, string Label, string? DataType = null, string? Description = null);

    /// <summary>What one wired input port receives~ 🔌.</summary>
    /// <param name="PortName">The target input port on the configured node.</param>
    /// <param name="SourceNodeId">The source node id.</param>
    /// <param name="SourceNodeName">The source node display name.</param>
    /// <param name="SourcePortName">The source output port.</param>
    /// <param name="Keys">Expandable sub-keys when the arriving value has a known shape (merged / named FanIn); empty otherwise.</param>
    public sealed record Incoming(
        string PortName,
        string SourceNodeId,
        string SourceNodeName,
        string SourcePortName,
        IReadOnlyList<Key> Keys);

    /// <summary>
    /// The wired inputs of a node, one entry per incoming connection, with expandable sub-keys
    /// where the arriving shape is derivable~ 🔌.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="nodeId">The node being configured.</param>
    /// <returns>The incoming entries, in connection order.</returns>
    public static IReadOnlyList<Incoming> IncomingFor(DesignerDocument document, string nodeId)
    {
        var result = new List<Incoming>();
        foreach (var c in document.Connections)
        {
            if (c.TargetNodeId != nodeId)
            {
                continue;
            }

            var source = document.FindNode(c.SourceNodeId);
            var isSelfInput = string.Equals(c.TargetPortName, "input", StringComparison.OrdinalIgnoreCase);
            result.Add(new Incoming(
                c.TargetPortName,
                c.SourceNodeId,
                source?.Name ?? c.SourceNodeId,
                c.SourcePortName,
                source is null
                    ? Array.Empty<Key>()
                    : SubKeysFor(
                        document,
                        source,
                        c.SourcePortName,
                        prefix: isSelfInput ? "input" : $"{c.SourceNodeId}.{c.SourcePortName}")));
        }

        return result;
    }

    /// <summary>
    /// Every token addressable from a node for one upstream source node: the base port tokens,
    /// plus sub-key expansion for merged nodes and named FanIns~ 🎯.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="source">The upstream node.</param>
    /// <returns>The keys, base ports first.</returns>
    public static IReadOnlyList<Key> AddressableKeys(DesignerDocument document, DesignerNode source)
    {
        var keys = new List<Key>();
        foreach (var port in NodePorts.Outputs(source))
        {
            var def = source.Schema?.Outputs.FirstOrDefault(p => p.Name == port);
            keys.Add(new Key(
                VariableTokens.OutputToken(source.Id, port),
                $"{source.Name} · {port}",
                def?.DataType,
                def?.Description));

            keys.AddRange(SubKeysFor(document, source, port, prefix: $"{source.Id}.{port}"));
        }

        return keys;
    }

    /// <summary>
    /// The derivable sub-keys of the value a source port emits: a <b>merged</b> node's single
    /// <c>output</c> object contains one key per pre-collapse schema port; a <b>named</b>-mode
    /// FanIn's <c>result</c> contains the computed branch keys. Everything else: unknown (empty —
    /// schema depth stops here, T1.2)~ 🔑.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="source">The source node.</param>
    /// <param name="sourcePort">The port the value leaves on.</param>
    /// <param name="prefix">The token path prefix addressing that value (no braces).</param>
    /// <returns>The sub-keys, or empty.</returns>
    public static IReadOnlyList<Key> SubKeysFor(
        DesignerDocument document,
        DesignerNode source,
        string sourcePort,
        string prefix)
    {
        // Merged node: 'output' = { <each pre-collapse schema port>: … } (F2).
        if (OutputShapingUx.IsMerged(source)
            && string.Equals(sourcePort, OutputShapingUx.MergedPortName, StringComparison.OrdinalIgnoreCase)
            && source.Schema is { Outputs.Count: > 0 } schema)
        {
            return schema.Outputs
                .Select(p => new Key($"{{{{{prefix}.{p.Name}}}}}", $"{prefix}.{p.Name}", p.DataType, p.Description))
                .ToList();
        }

        // Named-mode FanIn: 'result' = { <computed branch key>: … } (FanInShape mirrors the engine).
        if (FanInShape.IsFanIn(source) && sourcePort == "result" && FanInShape.Mode(source) == "named")
        {
            var branches = FanInShape.Branches(document, source.Id);
            var names = FanInShape.NamedKeys(branches);
            return branches
                .Select((b, i) => new Key(
                    $"{{{{{prefix}.{names[i]}}}}}",
                    $"{prefix}.{names[i]}",
                    null,
                    $"Branch {i + 1}: {b.SourceNodeName} · {b.SourcePortName}"))
                .ToList();
        }

        return Array.Empty<Key>();
    }

    /// <summary>Whether the node's own <c>input</c> port is wired (so <c>{{input}}</c> will resolve)~ 🔌.</summary>
    /// <param name="document">The document.</param>
    /// <param name="nodeId">The node.</param>
    /// <returns>True when a connection targets the node's <c>input</c> port.</returns>
    public static bool SelfInputWired(DesignerDocument document, string nodeId)
        => document.Connections.Any(c =>
            c.TargetNodeId == nodeId
            && string.Equals(c.TargetPortName, "input", StringComparison.OrdinalIgnoreCase));
}
