// <copyright file="SelectionState.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 🎯 Phase 3.3.b.1 — Tracks selected node ids and connection keys. Framework-free~ ✨.
/// </summary>
public sealed class SelectionState
{
    private readonly HashSet<string> nodes = new(StringComparer.Ordinal);
    private readonly HashSet<string> connections = new(StringComparer.Ordinal);

    /// <summary>Raised whenever the selection changes~ 🔔.</summary>
    public event Action? Changed;

    /// <summary>Gets the selected node ids.</summary>
    public IReadOnlyCollection<string> Nodes => this.nodes;

    /// <summary>Gets the selected connection keys.</summary>
    public IReadOnlyCollection<string> Connections => this.connections;

    /// <summary>Gets a value indicating whether nothing is selected.</summary>
    public bool IsEmpty => this.nodes.Count == 0 && this.connections.Count == 0;

    /// <summary>Returns whether a node is selected~ 🔍.</summary>
    /// <param name="nodeId">The node id.</param>
    /// <returns>True if selected.</returns>
    public bool IsNodeSelected(string nodeId) => this.nodes.Contains(nodeId);

    /// <summary>Returns whether a connection is selected~ 🔍.</summary>
    /// <param name="key">The connection key.</param>
    /// <returns>True if selected.</returns>
    public bool IsConnectionSelected(string key) => this.connections.Contains(key);

    /// <summary>Selects a single node, clearing all else~ 🎯.</summary>
    /// <param name="nodeId">The node id.</param>
    public void SelectNode(string nodeId)
    {
        this.nodes.Clear();
        this.connections.Clear();
        this.nodes.Add(nodeId);
        this.Changed?.Invoke();
    }

    /// <summary>Toggles a node in the selection (multi-select)~ ➕.</summary>
    /// <param name="nodeId">The node id.</param>
    public void ToggleNode(string nodeId)
    {
        if (!this.nodes.Remove(nodeId))
        {
            this.nodes.Add(nodeId);
        }

        this.Changed?.Invoke();
    }

    /// <summary>Selects a single connection, clearing all else~ 🔗.</summary>
    /// <param name="key">The connection key.</param>
    public void SelectConnection(string key)
    {
        this.nodes.Clear();
        this.connections.Clear();
        this.connections.Add(key);
        this.Changed?.Invoke();
    }

    /// <summary>Replaces the node selection with the given set~ 🎯.</summary>
    /// <param name="nodeIds">The node ids.</param>
    public void SetNodes(IEnumerable<string> nodeIds)
    {
        this.nodes.Clear();
        this.connections.Clear();
        foreach (var id in nodeIds)
        {
            this.nodes.Add(id);
        }

        this.Changed?.Invoke();
    }

    /// <summary>Selects all nodes in the document~ 🌐.</summary>
    /// <param name="document">The document.</param>
    public void SelectAll(DesignerDocument document)
        => this.SetNodes(document.Nodes.Select(n => n.Id));

    /// <summary>Clears the selection~ 🧹.</summary>
    public void Clear()
    {
        if (this.IsEmpty)
        {
            return;
        }

        this.nodes.Clear();
        this.connections.Clear();
        this.Changed?.Invoke();
    }

    /// <summary>Removes ids/keys that no longer exist (after deletion)~ 🧹.</summary>
    /// <param name="document">The document.</param>
    public void Prune(DesignerDocument document)
    {
        var liveNodes = new HashSet<string>(document.Nodes.Select(n => n.Id));
        var liveConns = new HashSet<string>(document.Connections.Select(c => c.Key));
        var removed = this.nodes.RemoveWhere(n => !liveNodes.Contains(n)) > 0;
        removed |= this.connections.RemoveWhere(c => !liveConns.Contains(c)) > 0;
        if (removed)
        {
            this.Changed?.Invoke();
        }
    }
}
