// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using System.Text.Json.Serialization;

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
/// <param name="BufferCapacity">
/// Optional bounded-channel capacity for a <b>streaming</b> connection (Phase 5.1). 🌊
/// Null means "use the workflow default, else the engine default (64)". Ignored on non-streaming
/// connections. Smaller values tighten memory, larger values smooth bursty producers~ ✨.
/// </param>
/// <param name="Kind">
/// <see cref="ConnectionKind.Data"/> (default) carries the source port's value into
/// <paramref name="TargetPortName"/>; <see cref="ConnectionKind.RunAfter"/> only sequences: the
/// target waits for (and is skipped with) the source port like any edge, but receives no data, and
/// <paramref name="TargetPortName"/> is empty (plan 09). Omitted from JSON when <c>Data</c>.
/// </param>
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
    int Priority = 0,
    int? BufferCapacity = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    ConnectionKind Kind = ConnectionKind.Data)
{
    /// <summary>Gets a value indicating whether this edge only sequences (no data). ⏭️.</summary>
    [JsonIgnore]
    public bool IsRunAfter => this.Kind == ConnectionKind.RunAfter;
}

/// <summary>
/// What a connection does: carry data into a port, or only order execution (plan 09). 🔗.
/// </summary>
/// <remarks>Serialized by name in every serializer (persistence has no global enum converter).</remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConnectionKind
{
    /// <summary>The source port's value is delivered to the target port.</summary>
    Data = 0,

    /// <summary>"Run after": sequencing only — no value is delivered, no target port.</summary>
    RunAfter = 1,
}
