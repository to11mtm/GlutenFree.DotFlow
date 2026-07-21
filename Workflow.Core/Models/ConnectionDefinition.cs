// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using System.Text.Json;

namespace Workflow.Core.Models;

/// <summary>
/// Defines a connection between two nodes in a workflow. 🔗.
/// </summary>
/// <param name="SourceNodeId">The ID of the source node. 📤.</param>
/// <param name="SourcePortName">The name of the output port on the source node. 🚪.</param>
/// <param name="TargetNodeId">The ID of the target node. 📥.</param>
/// <param name="TargetPortName">The name of the input port on the target node. 🚪.</param>
/// <param name="Condition">Optional condition expression for conditional routing. Can be null for unconditional. 🔀.</param>
/// <param name="Priority">Priority for parallel execution (lower numbers execute first). Default is 0. 📊.</param>
/// <remarks>
/// CopilotNote: Connections represent the "edges" in our workflow graph!
/// Multiple connections from the same source create parallel execution paths, nya~! 💫
/// Conditions are evaluated at runtime to determine if the connection should be followed. 💖.
/// </remarks>
public record ConnectionDefinition(
    string SourceNodeId,
    string SourcePortName,
    string TargetNodeId,
    string TargetPortName,
    string? Condition = null,
    int Priority = 0);
