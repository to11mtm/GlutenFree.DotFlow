// <copyright file="DesignerClipboard.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 📋 Phase 3.3.b.4 — In-app clipboard for copy/paste: holds cloned nodes plus the connections
/// *internal* to the copied set. Framework-free~ ✨.
/// </summary>
public sealed class DesignerClipboard
{
    private readonly List<DesignerNode> nodes = new();
    private readonly List<DesignerConnection> connections = new();

    /// <summary>Gets a value indicating whether the clipboard holds anything.</summary>
    public bool HasContent => this.nodes.Count > 0;

    /// <summary>Copies the given node ids (and internal edges) from a document~ 📄.</summary>
    /// <param name="document">The source document.</param>
    /// <param name="nodeIds">The node ids to copy.</param>
    public void Copy(DesignerDocument document, IEnumerable<string> nodeIds)
    {
        var ids = new HashSet<string>(nodeIds);
        this.nodes.Clear();
        this.connections.Clear();

        foreach (var n in document.Nodes.Where(n => ids.Contains(n.Id)))
        {
            this.nodes.Add(n.Clone());
        }

        foreach (var c in document.Connections.Where(c => ids.Contains(c.SourceNodeId) && ids.Contains(c.TargetNodeId)))
        {
            this.connections.Add(c.Clone());
        }
    }

    /// <summary>
    /// Produces paste-ready clones with fresh ids (offset) plus re-wired internal connections~ 📌.
    /// </summary>
    /// <param name="existingIds">Ids already in the target document (mutated with the new ids).</param>
    /// <param name="offset">Position offset applied to each pasted node.</param>
    /// <returns>The new nodes and re-wired connections.</returns>
    public (List<DesignerNode> Nodes, List<DesignerConnection> Connections) BuildPaste(HashSet<string> existingIds, double offset = 40)
    {
        var idMap = new Dictionary<string, string>();
        var newNodes = new List<DesignerNode>();

        foreach (var n in this.nodes)
        {
            var clone = n.Clone();
            clone.Id = NodeIdGenerator.Generate(n.ModuleId, existingIds);
            existingIds.Add(clone.Id);
            clone.X = n.X + offset;
            clone.Y = n.Y + offset;
            idMap[n.Id] = clone.Id;
            newNodes.Add(clone);
        }

        var newConns = new List<DesignerConnection>();
        foreach (var c in this.connections)
        {
            if (idMap.TryGetValue(c.SourceNodeId, out var ns) && idMap.TryGetValue(c.TargetNodeId, out var nt))
            {
                newConns.Add(new DesignerConnection
                {
                    SourceNodeId = ns,
                    SourcePortName = c.SourcePortName,
                    TargetNodeId = nt,
                    TargetPortName = c.TargetPortName,
                    Condition = c.Condition,
                    Priority = c.Priority,
                });
            }
        }

        return (newNodes, newConns);
    }
}
