// <copyright file="Commands.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State.Commands;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>➕ Phase 3.3.b.0 — Adds a node to the document~ ✨.</summary>
public sealed class AddNodeCommand : IDesignerCommand
{
    private readonly DesignerNode node;

    /// <summary>Initializes a new instance of the <see cref="AddNodeCommand"/> class~ ➕.</summary>
    /// <param name="node">The node to add.</param>
    public AddNodeCommand(DesignerNode node) => this.node = node;

    /// <inheritdoc/>
    public string Description => $"Add {this.node.Name}";

    /// <inheritdoc/>
    public void Do(DesignerDocument document) => document.Nodes.Add(this.node);

    /// <inheritdoc/>
    public void Undo(DesignerDocument document) => document.Nodes.RemoveAll(n => n.Id == this.node.Id);
}

/// <summary>🗑️ Phase 3.3.b.1 — Removes nodes and their attached connections (restored on undo)~ ✨.</summary>
public sealed class RemoveNodesCommand : IDesignerCommand
{
    private readonly HashSet<string> nodeIds;
    private List<DesignerNode> removedNodes = new();
    private List<DesignerConnection> removedConnections = new();

    /// <summary>Initializes a new instance of the <see cref="RemoveNodesCommand"/> class~ 🗑️.</summary>
    /// <param name="nodeIds">The node ids to remove.</param>
    public RemoveNodesCommand(IEnumerable<string> nodeIds)
        => this.nodeIds = new HashSet<string>(nodeIds);

    /// <inheritdoc/>
    public string Description => this.nodeIds.Count == 1 ? "Delete node" : $"Delete {this.nodeIds.Count} nodes";

    /// <inheritdoc/>
    public void Do(DesignerDocument document)
    {
        this.removedNodes = document.Nodes.Where(n => this.nodeIds.Contains(n.Id)).ToList();
        this.removedConnections = document.Connections
            .Where(c => this.nodeIds.Contains(c.SourceNodeId) || this.nodeIds.Contains(c.TargetNodeId))
            .ToList();

        document.Nodes.RemoveAll(n => this.nodeIds.Contains(n.Id));
        document.Connections.RemoveAll(c => this.nodeIds.Contains(c.SourceNodeId) || this.nodeIds.Contains(c.TargetNodeId));
    }

    /// <inheritdoc/>
    public void Undo(DesignerDocument document)
    {
        document.Nodes.AddRange(this.removedNodes);
        document.Connections.AddRange(this.removedConnections);
    }
}

/// <summary>↔️ Phase 3.3.b.1 — Moves one or more nodes by absolute before/after positions (one per drag)~ ✨.</summary>
public sealed class MoveNodesCommand : IDesignerCommand
{
    private readonly Dictionary<string, (double X, double Y)> before;
    private readonly Dictionary<string, (double X, double Y)> after;

    /// <summary>Initializes a new instance of the <see cref="MoveNodesCommand"/> class~ ↔️.</summary>
    /// <param name="before">Node id → original position.</param>
    /// <param name="after">Node id → new position.</param>
    public MoveNodesCommand(Dictionary<string, (double X, double Y)> before, Dictionary<string, (double X, double Y)> after)
    {
        this.before = before;
        this.after = after;
    }

    /// <inheritdoc/>
    public string Description => this.after.Count == 1 ? "Move node" : $"Move {this.after.Count} nodes";

    /// <inheritdoc/>
    public void Do(DesignerDocument document) => Apply(document, this.after);

    /// <inheritdoc/>
    public void Undo(DesignerDocument document) => Apply(document, this.before);

    private static void Apply(DesignerDocument document, Dictionary<string, (double X, double Y)> positions)
    {
        foreach (var (id, pos) in positions)
        {
            var node = document.FindNode(id);
            if (node is not null)
            {
                node.X = pos.X;
                node.Y = pos.Y;
            }
        }
    }
}

/// <summary>✏️ Phase 3.3.b.3 — Replaces a node's property bag (before/after)~ ✨.</summary>
public sealed class EditNodePropertiesCommand : IDesignerCommand
{
    private readonly string nodeId;
    private readonly Dictionary<string, JsonElement> before;
    private readonly Dictionary<string, JsonElement> after;

    /// <summary>Initializes a new instance of the <see cref="EditNodePropertiesCommand"/> class~ ✏️.</summary>
    /// <param name="nodeId">The node id.</param>
    /// <param name="before">The prior property bag.</param>
    /// <param name="after">The new property bag.</param>
    public EditNodePropertiesCommand(string nodeId, Dictionary<string, JsonElement> before, Dictionary<string, JsonElement> after)
    {
        this.nodeId = nodeId;
        this.before = before;
        this.after = after;
    }

    /// <inheritdoc/>
    public string Description => "Edit properties";

    /// <inheritdoc/>
    public void Do(DesignerDocument document) => Apply(document, this.after);

    /// <inheritdoc/>
    public void Undo(DesignerDocument document) => Apply(document, this.before);

    private void Apply(DesignerDocument document, Dictionary<string, JsonElement> props)
    {
        var node = document.FindNode(this.nodeId);
        if (node is null)
        {
            return;
        }

        node.Properties.Clear();
        foreach (var (k, v) in props)
        {
            node.Properties[k] = v;
        }
    }
}

/// <summary>🏷️ Phase 3.3.b.1 — Renames a node~ ✨.</summary>
public sealed class RenameNodeCommand : IDesignerCommand
{
    private readonly string nodeId;
    private readonly string before;
    private readonly string after;

    /// <summary>Initializes a new instance of the <see cref="RenameNodeCommand"/> class~ 🏷️.</summary>
    /// <param name="nodeId">The node id.</param>
    /// <param name="before">The old name.</param>
    /// <param name="after">The new name.</param>
    public RenameNodeCommand(string nodeId, string before, string after)
    {
        this.nodeId = nodeId;
        this.before = before;
        this.after = after;
    }

    /// <inheritdoc/>
    public string Description => "Rename node";

    /// <inheritdoc/>
    public void Do(DesignerDocument document)
    {
        var node = document.FindNode(this.nodeId);
        if (node is not null)
        {
            node.Name = this.after;
        }
    }

    /// <inheritdoc/>
    public void Undo(DesignerDocument document)
    {
        var node = document.FindNode(this.nodeId);
        if (node is not null)
        {
            node.Name = this.before;
        }
    }
}

/// <summary>🔗 Phase 3.3.b.2 — Adds a connection~ ✨.</summary>
public sealed class AddConnectionCommand : IDesignerCommand
{
    private readonly DesignerConnection connection;

    /// <summary>Initializes a new instance of the <see cref="AddConnectionCommand"/> class~ 🔗.</summary>
    /// <param name="connection">The connection to add.</param>
    public AddConnectionCommand(DesignerConnection connection) => this.connection = connection;

    /// <inheritdoc/>
    public string Description => "Add connection";

    /// <inheritdoc/>
    public void Do(DesignerDocument document) => document.Connections.Add(this.connection);

    /// <inheritdoc/>
    public void Undo(DesignerDocument document) => document.Connections.RemoveAll(c => c.Key == this.connection.Key);
}

/// <summary>🗑️ Phase 3.3.b.1 — Removes connections by key (restored on undo)~ ✨.</summary>
public sealed class RemoveConnectionsCommand : IDesignerCommand
{
    private readonly HashSet<string> keys;
    private List<DesignerConnection> removed = new();

    /// <summary>Initializes a new instance of the <see cref="RemoveConnectionsCommand"/> class~ 🗑️.</summary>
    /// <param name="keys">The connection keys to remove.</param>
    public RemoveConnectionsCommand(IEnumerable<string> keys) => this.keys = new HashSet<string>(keys);

    /// <inheritdoc/>
    public string Description => "Delete connection";

    /// <inheritdoc/>
    public void Do(DesignerDocument document)
    {
        this.removed = document.Connections.Where(c => this.keys.Contains(c.Key)).ToList();
        document.Connections.RemoveAll(c => this.keys.Contains(c.Key));
    }

    /// <inheritdoc/>
    public void Undo(DesignerDocument document) => document.Connections.AddRange(this.removed);
}

/// <summary>✏️ Phase 3.3.b.1 — Edits a connection's condition/priority~ ✨.</summary>
public sealed class EditConnectionCommand : IDesignerCommand
{
    private readonly string key;
    private readonly string? beforeCondition;
    private readonly string? afterCondition;

    /// <summary>Initializes a new instance of the <see cref="EditConnectionCommand"/> class~ ✏️.</summary>
    /// <param name="key">The connection key.</param>
    /// <param name="beforeCondition">The old condition.</param>
    /// <param name="afterCondition">The new condition.</param>
    public EditConnectionCommand(string key, string? beforeCondition, string? afterCondition)
    {
        this.key = key;
        this.beforeCondition = beforeCondition;
        this.afterCondition = afterCondition;
    }

    /// <inheritdoc/>
    public string Description => "Edit connection";

    /// <inheritdoc/>
    public void Do(DesignerDocument document) => Set(document, this.afterCondition);

    /// <inheritdoc/>
    public void Undo(DesignerDocument document) => Set(document, this.beforeCondition);

    private void Set(DesignerDocument document, string? condition)
    {
        var conn = document.Connections.FirstOrDefault(c => c.Key == this.key);
        if (conn is not null)
        {
            conn.Condition = condition;
        }
    }
}

/// <summary>📝 Phase 3.3.b.3 — Edits workflow-level metadata (name/description/tags)~ ✨.</summary>
public sealed class EditWorkflowMetaCommand : IDesignerCommand
{
    private readonly (string Name, string? Description, List<string> Tags) before;
    private readonly (string Name, string? Description, List<string> Tags) after;

    /// <summary>Initializes a new instance of the <see cref="EditWorkflowMetaCommand"/> class~ 📝.</summary>
    /// <param name="before">The prior meta.</param>
    /// <param name="after">The new meta.</param>
    public EditWorkflowMetaCommand(
        (string Name, string? Description, List<string> Tags) before,
        (string Name, string? Description, List<string> Tags) after)
    {
        this.before = before;
        this.after = after;
    }

    /// <inheritdoc/>
    public string Description => "Edit workflow";

    /// <inheritdoc/>
    public void Do(DesignerDocument document) => Apply(document, this.after);

    /// <inheritdoc/>
    public void Undo(DesignerDocument document) => Apply(document, this.before);

    private static void Apply(DesignerDocument document, (string Name, string? Description, List<string> Tags) meta)
    {
        document.Name = meta.Name;
        document.Description = meta.Description;
        document.Tags.Clear();
        document.Tags.AddRange(meta.Tags);
    }
}

/// <summary>🧩 Phase 3.3.b.4 — Runs several commands as a single undoable unit (e.g. paste)~ ✨.</summary>
public sealed class CompositeCommand : IDesignerCommand
{
    private readonly IReadOnlyList<IDesignerCommand> commands;

    /// <summary>Initializes a new instance of the <see cref="CompositeCommand"/> class~ 🧩.</summary>
    /// <param name="description">The combined description.</param>
    /// <param name="commands">The child commands (applied in order; undone in reverse).</param>
    public CompositeCommand(string description, IReadOnlyList<IDesignerCommand> commands)
    {
        this.Description = description;
        this.commands = commands;
    }

    /// <inheritdoc/>
    public string Description { get; }

    /// <inheritdoc/>
    public void Do(DesignerDocument document)
    {
        foreach (var c in this.commands)
        {
            c.Do(document);
        }
    }

    /// <inheritdoc/>
    public void Undo(DesignerDocument document)
    {
        for (var i = this.commands.Count - 1; i >= 0; i--)
        {
            this.commands[i].Undo(document);
        }
    }
}
