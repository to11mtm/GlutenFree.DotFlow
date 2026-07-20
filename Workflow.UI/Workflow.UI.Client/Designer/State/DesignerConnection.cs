// <copyright file="DesignerConnection.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using Workflow.UI.Client.Api.Dtos;

/// <summary>
/// 🔗 Phase 3.3.a.2 — A mutable canvas connection (edge). Framework-free~ ✨.
/// </summary>
public sealed class DesignerConnection
{
    /// <summary>Gets or sets the source node id.</summary>
    public string SourceNodeId { get; set; } = string.Empty;

    /// <summary>Gets or sets the source output port name.</summary>
    public string SourcePortName { get; set; } = string.Empty;

    /// <summary>Gets or sets the target node id.</summary>
    public string TargetNodeId { get; set; } = string.Empty;

    /// <summary>Gets or sets the target input port name.</summary>
    public string TargetPortName { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional runtime condition expression.</summary>
    public string? Condition { get; set; }

    /// <summary>Gets or sets the ordering priority (lower first).</summary>
    public int Priority { get; set; }

    /// <summary>A stable identity key for selection/dedup: <c>src.port→tgt.port</c>~ 🔑.</summary>
    public string Key => $"{this.SourceNodeId}.{this.SourcePortName}→{this.TargetNodeId}.{this.TargetPortName}";

    /// <summary>Builds a designer connection from a wire DTO~ 🏗️.</summary>
    /// <param name="dto">The connection DTO.</param>
    /// <returns>The designer connection.</returns>
    public static DesignerConnection FromDto(ConnectionDto dto)
        => new()
        {
            SourceNodeId = dto.SourceNodeId,
            SourcePortName = dto.SourcePortName,
            TargetNodeId = dto.TargetNodeId,
            TargetPortName = dto.TargetPortName,
            Condition = dto.Condition,
            Priority = dto.Priority,
        };

    /// <summary>Projects this connection back to a wire DTO~ 📤.</summary>
    /// <returns>The connection DTO.</returns>
    public ConnectionDto ToDto()
        => new(this.SourceNodeId, this.SourcePortName, this.TargetNodeId, this.TargetPortName, this.Condition, this.Priority);

    /// <summary>Creates a clone~ 🧬.</summary>
    /// <returns>A cloned connection.</returns>
    public DesignerConnection Clone()
        => new()
        {
            SourceNodeId = this.SourceNodeId,
            SourcePortName = this.SourcePortName,
            TargetNodeId = this.TargetNodeId,
            TargetPortName = this.TargetPortName,
            Condition = this.Condition,
            Priority = this.Priority,
        };
}
