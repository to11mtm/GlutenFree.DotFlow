// <copyright file="DesignerNode.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System.Collections.Generic;
using System.Text.Json;
using Workflow.UI.Client.Api.Dtos;

/// <summary>
/// 🎯 Phase 3.3.a.2 — A mutable canvas node. Framework-free (no Blazor). Maps 1:1 to
/// <see cref="NodeDto"/> plus the resolved module <see cref="Schema"/> for rendering (D2/D5)~ ✨.
/// </summary>
public sealed class DesignerNode
{
    /// <summary>Gets or sets the node id (unique within the document).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the module id this node instantiates.</summary>
    public string ModuleId { get; set; } = string.Empty;

    /// <summary>Gets or sets the display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets the configured property values (name → JSON value).</summary>
    public Dictionary<string, JsonElement> Properties { get; } = new();

    /// <summary>Gets or sets the horizontal canvas position.</summary>
    public double X { get; set; }

    /// <summary>Gets or sets the vertical canvas position.</summary>
    public double Y { get; set; }

    /// <summary>Gets the metadata (e.g. <c>moduleVersion</c>, <c>ui.*</c>).</summary>
    public Dictionary<string, string> Metadata { get; } = new();

    /// <summary>Gets or sets the optional per-node timeout (seconds).</summary>
    public int? Timeout { get; set; }

    /// <summary>Gets or sets passthrough per-node error-handling JSON (not edited in MVP).</summary>
    public JsonElement? ErrorHandling { get; set; }

    /// <summary>Gets or sets passthrough retry-policy JSON (not edited in MVP).</summary>
    public JsonElement? RetryPolicy { get; set; }

    /// <summary>Gets or sets the optional region id.</summary>
    public string? RegionId { get; set; }

    /// <summary>Gets or sets the resolved module schema (null when the module is unknown/missing).</summary>
    public ModuleSchemaDto? Schema { get; set; }

    /// <summary>Gets a value indicating whether the module for this node was resolved.</summary>
    public bool IsModuleKnown => this.Schema is not null;

    /// <summary>Builds a designer node from a wire DTO, resolving its schema~ 🏗️.</summary>
    /// <param name="dto">The node DTO.</param>
    /// <param name="schema">The resolved module schema, or null if unknown.</param>
    /// <returns>The designer node.</returns>
    public static DesignerNode FromDto(NodeDto dto, ModuleSchemaDto? schema)
    {
        var node = new DesignerNode
        {
            Id = dto.Id,
            ModuleId = dto.ModuleId,
            Name = dto.Name,
            X = dto.Position?.X ?? 0,
            Y = dto.Position?.Y ?? 0,
            Timeout = dto.Timeout,
            ErrorHandling = dto.ErrorHandling,
            RetryPolicy = dto.RetryPolicy,
            RegionId = dto.RegionId,
            Schema = schema,
        };

        foreach (var (k, v) in dto.Properties)
        {
            node.Properties[k] = v;
        }

        if (dto.Metadata is not null)
        {
            foreach (var (k, v) in dto.Metadata)
            {
                node.Metadata[k] = v;
            }
        }

        return node;
    }

    /// <summary>Projects this node back to a wire DTO~ 📤.</summary>
    /// <returns>The node DTO.</returns>
    public NodeDto ToDto()
        => new(
            this.Id,
            this.ModuleId,
            this.Name,
            new Dictionary<string, JsonElement>(this.Properties),
            new PositionDto(this.X, this.Y),
            this.ErrorHandling,
            this.Timeout,
            this.RetryPolicy,
            this.Metadata.Count > 0 ? new Dictionary<string, string>(this.Metadata) : null,
            this.RegionId);

    /// <summary>Creates a deep-ish clone (properties + metadata copied)~ 🧬.</summary>
    /// <returns>A cloned node.</returns>
    public DesignerNode Clone()
    {
        var clone = new DesignerNode
        {
            Id = this.Id,
            ModuleId = this.ModuleId,
            Name = this.Name,
            X = this.X,
            Y = this.Y,
            Timeout = this.Timeout,
            ErrorHandling = this.ErrorHandling,
            RetryPolicy = this.RetryPolicy,
            RegionId = this.RegionId,
            Schema = this.Schema,
        };

        foreach (var (k, v) in this.Properties)
        {
            clone.Properties[k] = v;
        }

        foreach (var (k, v) in this.Metadata)
        {
            clone.Metadata[k] = v;
        }

        return clone;
    }
}
