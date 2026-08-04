// <copyright file="Commands.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State.Commands;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>âž• Phase 3.3.b.0 â€” Adds a node to the document~ âœ¨.</summary>
public sealed class AddNodeCommand : IDesignerCommand
{
    private readonly DesignerNode node;

    /// <summary>Initializes a new instance of the <see cref="AddNodeCommand"/> class~ âž•.</summary>
    /// <param name="node">The node to add.</param>
    public AddNodeCommand(DesignerNode node) => this.node = node;

    /// <inheritdoc/>
    public string Description => $"Add {this.node.Name}";

    /// <inheritdoc/>
    public void Do(DesignerDocument document) => document.Nodes.Add(this.node);

    /// <inheritdoc/>
    public void Undo(DesignerDocument document) => document.Nodes.RemoveAll(n => n.Id == this.node.Id);
}

/// <summary>ðŸ—‘ï¸ Phase 3.3.b.1 â€” Removes nodes and their attached connections (restored on undo)~ âœ¨.</summary>
public sealed class RemoveNodesCommand : IDesignerCommand
{
    private readonly HashSet<string> nodeIds;
    private List<DesignerNode> removedNodes = new();
    private List<DesignerConnection> removedConnections = new();

    /// <summary>Initializes a new instance of the <see cref="RemoveNodesCommand"/> class~ ðŸ—‘ï¸.</summary>
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

/// <summary>â†”ï¸ Phase 3.3.b.1 â€” Moves one or more nodes by absolute before/after positions (one per drag)~ âœ¨.</summary>
public sealed class MoveNodesCommand : IDesignerCommand
{
    private readonly Dictionary<string, (double X, double Y)> before;
    private readonly Dictionary<string, (double X, double Y)> after;

    /// <summary>Initializes a new instance of the <see cref="MoveNodesCommand"/> class~ â†”ï¸.</summary>
    /// <param name="before">Node id â†’ original position.</param>
    /// <param name="after">Node id â†’ new position.</param>
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

/// <summary>âœï¸ Phase 3.3.b.3 â€” Replaces a node's property bag (before/after)~ âœ¨.</summary>
public sealed class EditNodePropertiesCommand : IDesignerCommand
{
    private readonly string nodeId;
    private readonly Dictionary<string, JsonElement> before;
    private readonly Dictionary<string, JsonElement> after;

    /// <summary>Initializes a new instance of the <see cref="EditNodePropertiesCommand"/> class~ âœï¸.</summary>
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

/// <summary>ðŸ·ï¸ Phase 3.3.b.1 â€” Renames a node~ âœ¨.</summary>
public sealed class RenameNodeCommand : IDesignerCommand
{
    private readonly string nodeId;
    private readonly string before;
    private readonly string after;

    /// <summary>Initializes a new instance of the <see cref="RenameNodeCommand"/> class~ ðŸ·ï¸.</summary>
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

/// <summary>ðŸ”— Phase 3.3.b.2 â€” Adds a connection~ âœ¨.</summary>
public sealed class AddConnectionCommand : IDesignerCommand
{
    private readonly DesignerConnection connection;

    /// <summary>Initializes a new instance of the <see cref="AddConnectionCommand"/> class~ ðŸ”—.</summary>
    /// <param name="connection">The connection to add.</param>
    public AddConnectionCommand(DesignerConnection connection) => this.connection = connection;

    /// <inheritdoc/>
    public string Description => "Add connection";

    /// <inheritdoc/>
    public void Do(DesignerDocument document) => document.Connections.Add(this.connection);

    /// <inheritdoc/>
    public void Undo(DesignerDocument document) => document.Connections.RemoveAll(c => c.Key == this.connection.Key);
}

/// <summary>ðŸ—‘ï¸ Phase 3.3.b.1 â€” Removes connections by key (restored on undo)~ âœ¨.</summary>
public sealed class RemoveConnectionsCommand : IDesignerCommand
{
    private readonly HashSet<string> keys;
    private List<DesignerConnection> removed = new();

    /// <summary>Initializes a new instance of the <see cref="RemoveConnectionsCommand"/> class~ ðŸ—‘ï¸.</summary>
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

/// <summary>âœï¸ Phase 3.3.b.1 â€” Edits a connection's condition/priority~ âœ¨.</summary>
public sealed class EditConnectionCommand : IDesignerCommand
{
    private readonly string key;
    private readonly string? beforeCondition;
    private readonly string? afterCondition;

    /// <summary>Initializes a new instance of the <see cref="EditConnectionCommand"/> class~ âœï¸.</summary>
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

/// <summary>ðŸ“ Phase 3.3.b.3 â€” Edits workflow-level metadata (name/description/tags)~ âœ¨.</summary>
public sealed class EditWorkflowMetaCommand : IDesignerCommand
{
    private readonly (string Name, string? Description, List<string> Tags) before;
    private readonly (string Name, string? Description, List<string> Tags) after;

    /// <summary>Initializes a new instance of the <see cref="EditWorkflowMetaCommand"/> class~ ðŸ“.</summary>
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

/// <summary>ðŸ’¾ Phase 3.5 (V1.3) â€” Declares a new workflow variable~ âž•.</summary>
public sealed class AddVariableCommand : IDesignerCommand
{
    private readonly string name;
    private readonly JsonElement declaration;

    /// <summary>Initializes a new instance of the <see cref="AddVariableCommand"/> class~ âž•.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="declaration">The declaration JSON.</param>
    public AddVariableCommand(string name, JsonElement declaration)
    {
        this.name = name;
        this.declaration = declaration;
    }

    /// <inheritdoc/>
    public string Description => $"Add variable {this.name}";

    /// <inheritdoc/>
    public void Do(DesignerDocument document) => document.Variables[this.name] = this.declaration;

    /// <inheritdoc/>
    public void Undo(DesignerDocument document) => document.Variables.Remove(this.name);
}

/// <summary>ðŸ’¾ Phase 3.5 (V1.3) â€” Removes a variable declaration~ ðŸ—‘ï¸.</summary>
public sealed class RemoveVariableCommand : IDesignerCommand
{
    private readonly string name;
    private readonly JsonElement before;

    /// <summary>Initializes a new instance of the <see cref="RemoveVariableCommand"/> class~ ðŸ—‘ï¸.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="before">The declaration being removed, kept so undo restores it exactly.</param>
    public RemoveVariableCommand(string name, JsonElement before)
    {
        this.name = name;
        this.before = before;
    }

    /// <inheritdoc/>
    public string Description => $"Remove variable {this.name}";

    /// <inheritdoc/>
    public void Do(DesignerDocument document) => document.Variables.Remove(this.name);

    /// <inheritdoc/>
    public void Undo(DesignerDocument document) => document.Variables[this.name] = this.before;
}

/// <summary>
/// ðŸ’¾ Phase 3.5 (V1.3) â€” Edits a variable declaration, including renaming it. A rename changes the
/// map key, so this is expressed as "drop the old key, write the new one" in both directions~ âœï¸.
/// </summary>
public sealed class EditVariableCommand : IDesignerCommand
{
    private readonly string beforeName;
    private readonly JsonElement beforeDeclaration;
    private readonly string afterName;
    private readonly JsonElement afterDeclaration;

    /// <summary>Initializes a new instance of the <see cref="EditVariableCommand"/> class~ âœï¸.</summary>
    /// <param name="beforeName">The prior name.</param>
    /// <param name="beforeDeclaration">The prior declaration.</param>
    /// <param name="afterName">The new name (may equal the prior name).</param>
    /// <param name="afterDeclaration">The new declaration.</param>
    public EditVariableCommand(
        string beforeName,
        JsonElement beforeDeclaration,
        string afterName,
        JsonElement afterDeclaration)
    {
        this.beforeName = beforeName;
        this.beforeDeclaration = beforeDeclaration;
        this.afterName = afterName;
        this.afterDeclaration = afterDeclaration;
    }

    /// <inheritdoc/>
    public string Description => string.Equals(this.beforeName, this.afterName, System.StringComparison.Ordinal)
        ? $"Edit variable {this.afterName}"
        : $"Rename variable {this.beforeName} â†’ {this.afterName}";

    /// <inheritdoc/>
    public void Do(DesignerDocument document)
    {
        document.Variables.Remove(this.beforeName);
        document.Variables[this.afterName] = this.afterDeclaration;
    }

    /// <inheritdoc/>
    public void Undo(DesignerDocument document)
    {
        document.Variables.Remove(this.afterName);
        document.Variables[this.beforeName] = this.beforeDeclaration;
    }
}

/// <summary>ðŸ§© Phase 3.3.b.4 â€” Runs several commands as a single undoable unit (e.g. paste)~ âœ¨.</summary>
public sealed class CompositeCommand : IDesignerCommand
{
    private readonly IReadOnlyList<IDesignerCommand> commands;

    /// <summary>Initializes a new instance of the <see cref="CompositeCommand"/> class~ ðŸ§©.</summary>
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

/// <summary>
/// ðŸ”€ Input-shape hinting T8 â€” moves one of a node's incoming connections up or down among that
/// node's branches. Branch order is connection <em>declaration</em> order and drives FanIn's
/// merge/first/last modes, so reordering is a real semantic edit and therefore undoable~ âœ¨.
/// </summary>
public sealed class ReorderIncomingConnectionCommand : IDesignerCommand
{
    private readonly string nodeId;
    private readonly int fromBranchIndex;
    private readonly int toBranchIndex;

    /// <summary>Initializes a new instance of the <see cref="ReorderIncomingConnectionCommand"/> class~ ðŸ”€.</summary>
    /// <param name="nodeId">The target node whose incoming branches are reordered.</param>
    /// <param name="fromBranchIndex">The branch's current position (0-based, connection order).</param>
    /// <param name="toBranchIndex">The branch's new position.</param>
    public ReorderIncomingConnectionCommand(string nodeId, int fromBranchIndex, int toBranchIndex)
    {
        this.nodeId = nodeId;
        this.fromBranchIndex = fromBranchIndex;
        this.toBranchIndex = toBranchIndex;
    }

    /// <inheritdoc/>
    public string Description => "Reorder branch";

    /// <inheritdoc/>
    public void Do(DesignerDocument document) => Move(document, this.fromBranchIndex, this.toBranchIndex);

    /// <inheritdoc/>
    public void Undo(DesignerDocument document) => Move(document, this.toBranchIndex, this.fromBranchIndex);

    private void Move(DesignerDocument document, int from, int to)
    {
        // The node's incoming connections, as positions within document.Connections~
        var positions = new List<int>();
        for (var i = 0; i < document.Connections.Count; i++)
        {
            if (document.Connections[i].TargetNodeId == this.nodeId)
            {
                positions.Add(i);
            }
        }

        if (from < 0 || from >= positions.Count || to < 0 || to >= positions.Count || from == to)
        {
            return;
        }

        // Swap the connections occupying the two branch slots â€” sibling connections keep their
        // document positions, so unrelated ordering is untouched~
        var a = positions[from];
        var b = positions[to];
        (document.Connections[a], document.Connections[b]) = (document.Connections[b], document.Connections[a]);
    }
}

/// <summary>
/// 💾 Split preview V3 — sets or removes one node metadata entry (e.g. the designer-only
/// <c>ui.sampleInput</c> sample). Null means absent on either side; undo restores exactly~ ✨.
/// </summary>
public sealed class EditNodeMetadataCommand : IDesignerCommand
{
    private readonly string nodeId;
    private readonly string key;
    private readonly string? before;
    private readonly string? after;

    /// <summary>Initializes a new instance of the <see cref="EditNodeMetadataCommand"/> class~ 💾.</summary>
    /// <param name="nodeId">The node.</param>
    /// <param name="key">The metadata key.</param>
    /// <param name="before">The previous value (null = absent).</param>
    /// <param name="after">The new value (null = remove).</param>
    public EditNodeMetadataCommand(string nodeId, string key, string? before, string? after)
    {
        this.nodeId = nodeId;
        this.key = key;
        this.before = before;
        this.after = after;
    }

    /// <inheritdoc/>
    public string Description => "Edit node metadata";

    /// <inheritdoc/>
    public void Do(DesignerDocument document) => Apply(document, this.after);

    /// <inheritdoc/>
    public void Undo(DesignerDocument document) => Apply(document, this.before);

    private void Apply(DesignerDocument document, string? value)
    {
        if (document.FindNode(this.nodeId) is not { } node)
        {
            return;
        }

        if (value is null)
        {
            node.Metadata.Remove(this.key);
        }
        else
        {
            node.Metadata[this.key] = value;
        }
    }
}
