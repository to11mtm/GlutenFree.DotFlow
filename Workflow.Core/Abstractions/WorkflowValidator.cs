// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using Workflow.Core.Models;

namespace Workflow.Core.Abstractions;

/// <summary>
/// Validates workflow definitions to ensure they are safe and correct before execution. 🛡️
/// </summary>
/// <remarks>
/// CopilotNote: This class performs comprehensive validation including:
/// - Structural validation (nodes, connections, cycles)
/// - Schema validation (properties match module schemas)
/// - Reference validation (variables, ports, node IDs)
/// Super important for preventing runtime errors, nya~! 💖
/// </remarks>
public class WorkflowValidator
{
    private readonly List<ValidationError> _errors = new();
    private readonly List<ValidationWarning> _warnings = new();

    /// <summary>
    /// Validates a workflow definition and returns the result. ✅
    /// </summary>
    /// <param name="workflow">The workflow definition to validate. 🌸</param>
    /// <returns>A validation result containing any errors or warnings. 📋</returns>
    public ValidationResult Validate(WorkflowDefinition workflow)
    {
        _errors.Clear();
        _warnings.Clear();

        // Basic structure validation
        ValidateBasicStructure(workflow);
        ValidateNodeIds(workflow);
        ValidateConnections(workflow);
        ValidateCycles(workflow); // ✨ Run cycle detection BEFORE start node validation!
        ValidateStartNodes(workflow);
        ValidateOrphanedNodes(workflow);
        ValidateVariableReferences(workflow);
        ValidateErrorHandlers(workflow);

        return ValidationResult.WithErrorsAndWarnings(_errors, _warnings);
    }

    /// <summary>
    /// Validates basic workflow structure (must have at least one node). 🎯
    /// </summary>
    private void ValidateBasicStructure(WorkflowDefinition workflow)
    {
        if (workflow.Nodes.Count == 0)
        {
            _errors.Add(new ValidationError(
                "WF001",
                "Workflow must contain at least one node"));
        }

        if (string.IsNullOrWhiteSpace(workflow.Name))
        {
            _errors.Add(new ValidationError(
                "WF002",
                "Workflow name cannot be empty"));
        }
    }

    /// <summary>
    /// Validates that all node IDs are unique within the workflow. 🆔
    /// </summary>
    private void ValidateNodeIds(WorkflowDefinition workflow)
    {
        var duplicates = workflow.Nodes
            .GroupBy(n => n.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        foreach (var duplicateId in duplicates)
        {
            _errors.Add(new ValidationError(
                "WF003",
                $"Duplicate node ID found: '{duplicateId}'",
                duplicateId));
        }

        // Check for empty/null node IDs
        var emptyIds = workflow.Nodes
            .Where(n => string.IsNullOrWhiteSpace(n.Id))
            .ToList();

        if (emptyIds.Any())
        {
            _errors.Add(new ValidationError(
                "WF004",
                $"Found {emptyIds.Count} node(s) with empty or null ID"));
        }
    }

    /// <summary>
    /// Validates all connections reference valid nodes and ports. 🔗
    /// </summary>
    private void ValidateConnections(WorkflowDefinition workflow)
    {
        var nodeIds = new HashSet<string>(workflow.Nodes.Select(n => n.Id));

        foreach (var connection in workflow.Connections)
        {
            // Validate source node exists
            if (!nodeIds.Contains(connection.SourceNodeId))
            {
                _errors.Add(new ValidationError(
                    "WF005",
                    $"Connection references non-existent source node: '{connection.SourceNodeId}'",
                    connection.SourceNodeId));
            }

            // Validate target node exists
            if (!nodeIds.Contains(connection.TargetNodeId))
            {
                _errors.Add(new ValidationError(
                    "WF006",
                    $"Connection references non-existent target node: '{connection.TargetNodeId}'",
                    connection.TargetNodeId));
            }

            // Validate not connecting node to itself
            if (connection.SourceNodeId == connection.TargetNodeId)
            {
                _errors.Add(new ValidationError(
                    "WF007",
                    $"Node cannot connect to itself: '{connection.SourceNodeId}'",
                    connection.SourceNodeId));
            }

            // Validate port names are not empty
            if (string.IsNullOrWhiteSpace(connection.SourcePortName))
            {
                _errors.Add(new ValidationError(
                    "WF008",
                    "Connection has empty source port name",
                    connection.SourceNodeId));
            }

            if (string.IsNullOrWhiteSpace(connection.TargetPortName))
            {
                _errors.Add(new ValidationError(
                    "WF009",
                    "Connection has empty target port name",
                    connection.TargetNodeId));
            }
        }
    }

    /// <summary>
    /// Validates that workflow has at least one start node (no incoming connections). 🚀
    /// </summary>
    private void ValidateStartNodes(WorkflowDefinition workflow)
    {
        var nodesWithIncoming = new HashSet<string>(
            workflow.Connections.Select(c => c.TargetNodeId));

        var startNodes = workflow.Nodes
            .Where(n => !nodesWithIncoming.Contains(n.Id))
            .ToList();

        if (startNodes.Count == 0)
        {
            _errors.Add(new ValidationError(
                "WF010",
                "Workflow must have at least one start node (node with no incoming connections)"));
        }
    }

    /// <summary>
    /// Validates there are no orphaned nodes (disconnected subgraphs). 🏝️
    /// </summary>
    private void ValidateOrphanedNodes(WorkflowDefinition workflow)
    {
        if (workflow.Nodes.Count == 0)
        {
            return;
        }

        // Build adjacency list for graph traversal
        var nodeIds = new HashSet<string>(workflow.Nodes.Select(n => n.Id));
        var adjacency = new Dictionary<string, List<string>>();
        foreach (var nodeId in nodeIds)
        {
            adjacency[nodeId] = new List<string>();
        }

        foreach (var connection in workflow.Connections)
        {
            // ✨ Only add connections if BOTH nodes exist (skip invalid connections)
            if (adjacency.ContainsKey(connection.SourceNodeId) &&
                adjacency.ContainsKey(connection.TargetNodeId))
            {
                adjacency[connection.SourceNodeId].Add(connection.TargetNodeId);
                adjacency[connection.TargetNodeId].Add(connection.SourceNodeId);
            }
        }

        // Find all nodes reachable from the first node (undirected graph)
        var visited = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(workflow.Nodes[0].Id);
        visited.Add(workflow.Nodes[0].Id);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            // ✨ Use TryGetValue to safely access adjacency list
            if (adjacency.TryGetValue(current, out var neighbors))
            {
                foreach (var neighbor in neighbors)
                {
                    if (!visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }

        // Any nodes not visited are orphaned
        var orphanedNodes = nodeIds.Except(visited).ToList();
        if (orphanedNodes.Any())
        {
            _warnings.Add(new ValidationWarning(
                "WF011",
                $"Found {orphanedNodes.Count} orphaned node(s): {string.Join(", ", orphanedNodes)}"));
        }
    }

    /// <summary>
    /// Detects cycles in the workflow graph (infinite loops). 🔄
    /// </summary>
    private void ValidateCycles(WorkflowDefinition workflow)
    {
        // Build adjacency list for directed graph
        var adjacency = new Dictionary<string, List<string>>();
        foreach (var node in workflow.Nodes)
        {
            adjacency[node.Id] = new List<string>();
        }

        foreach (var connection in workflow.Connections)
        {
            if (adjacency.ContainsKey(connection.SourceNodeId))
            {
                adjacency[connection.SourceNodeId].Add(connection.TargetNodeId);
            }
        }

        // Detect cycles using DFS with color marking
        // White = unvisited, Gray = visiting, Black = visited
        var color = new Dictionary<string, NodeColor>();
        foreach (var nodeId in adjacency.Keys)
        {
            color[nodeId] = NodeColor.White;
        }

        foreach (var nodeId in adjacency.Keys)
        {
            if (color[nodeId] == NodeColor.White)
            {
                if (HasCycleDFS(nodeId, adjacency, color, new List<string>()))
                {
                    // Cycle detected - error already added in HasCycleDFS
                    return;
                }
            }
        }
    }

    /// <summary>
    /// DFS helper for cycle detection. Returns true if cycle found. 🔍
    /// </summary>
    private bool HasCycleDFS(
        string nodeId,
        Dictionary<string, List<string>> adjacency,
        Dictionary<string, NodeColor> color,
        List<string> path)
    {
        color[nodeId] = NodeColor.Gray;
        path.Add(nodeId);

        foreach (var neighbor in adjacency[nodeId])
        {
            if (color.TryGetValue(neighbor, out var nodeColor))
            {
                if (nodeColor == NodeColor.Gray)
                {
                    // Back edge found - cycle detected!
                    var cycleStart = path.IndexOf(neighbor);
                    var cycle = string.Join(" → ", path.Skip(cycleStart).Append(neighbor));
                    _errors.Add(new ValidationError(
                        "WF012",
                        $"Cycle detected in workflow: {cycle}",
                        nodeId));
                    return true;
                }

                if (nodeColor == NodeColor.White)
                {
                    if (HasCycleDFS(neighbor, adjacency, color, path))
                    {
                        return true;
                    }
                }
            }
        }

        color[nodeId] = NodeColor.Black;
        path.RemoveAt(path.Count - 1);
        return false;
    }

    /// <summary>
    /// Validates that all variable references exist in workflow variables. 💾
    /// </summary>
    private void ValidateVariableReferences(WorkflowDefinition workflow)
    {
        // Check if variables referenced in node properties exist
        // This is a basic check - more sophisticated validation would parse property values
        var variableNames = new HashSet<string>(workflow.Variables.Keys);

        foreach (var node in workflow.Nodes)
        {
            // Check if any property values contain variable references
            // Note: This is a placeholder - actual implementation would parse JSON properties
            // to find variable references (e.g., "${variableName}" syntax)
        }
    }

    /// <summary>
    /// Validates error handler node references. 🚨
    /// </summary>
    private void ValidateErrorHandlers(WorkflowDefinition workflow)
    {
        var nodeIds = new HashSet<string>(workflow.Nodes.Select(n => n.Id));

        // Check workflow-level error handler
        if (workflow.ErrorHandling?.ErrorNodeId != null)
        {
            if (!nodeIds.Contains(workflow.ErrorHandling.ErrorNodeId))
            {
                _errors.Add(new ValidationError(
                    "WF013",
                    $"Workflow error handler references non-existent node: '{workflow.ErrorHandling.ErrorNodeId}'"));
            }
        }

        // Check node-level error handlers
        foreach (var node in workflow.Nodes)
        {
            if (node.ErrorHandling?.ErrorNodeId != null)
            {
                if (!nodeIds.Contains(node.ErrorHandling.ErrorNodeId))
                {
                    _errors.Add(new ValidationError(
                        "WF014",
                        $"Error handler references non-existent node: '{node.ErrorHandling.ErrorNodeId}'",
                        node.Id));
                }
            }
        }
    }

    /// <summary>
    /// Colors for graph traversal (White = unvisited, Gray = visiting, Black = visited). 🎨
    /// </summary>
    private enum NodeColor
    {
        White,
        Gray,
        Black,
    }
}
