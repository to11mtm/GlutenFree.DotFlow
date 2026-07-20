// <copyright file="DesignerDocument.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Workflow.UI.Client.Api.Dtos;

/// <summary>
/// 🧠 Phase 3.3.a.2 — The mutable client-side graph model, mirroring the wire
/// <see cref="WorkflowDto"/> 1:1 (D5). Framework-free (no Blazor) so a React port re-implements a
/// mechanical TS mirror (D2). All edits go through commands (D7) which mutate this document and
/// raise <see cref="Changed"/> for view invalidation~ ✨.
/// </summary>
public sealed class DesignerDocument
{
    /// <summary>Raised after any mutation, so views can re-render~ 🔔.</summary>
    public event Action? Changed;

    /// <summary>Gets or sets the workflow id (<see cref="Guid.Empty"/> for a not-yet-saved new workflow).</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the workflow name.</summary>
    public string Name { get; set; } = "Untitled workflow";

    /// <summary>Gets or sets the description.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the version string (e.g. <c>1.0.0</c>).</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>Gets the graph nodes.</summary>
    public List<DesignerNode> Nodes { get; } = new();

    /// <summary>Gets the graph connections.</summary>
    public List<DesignerConnection> Connections { get; } = new();

    /// <summary>Gets the workflow variables (name → raw definition JSON, passthrough).</summary>
    public Dictionary<string, JsonElement> Variables { get; } = new();

    /// <summary>Gets the workflow tags.</summary>
    public List<string> Tags { get; } = new();

    /// <summary>Gets or sets passthrough trigger JSON (not edited in MVP).</summary>
    public JsonElement? Trigger { get; set; }

    /// <summary>Gets or sets passthrough workflow-level error-handling JSON (not edited in MVP).</summary>
    public JsonElement? ErrorHandling { get; set; }

    /// <summary>Gets or sets the created timestamp (server-owned).</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Gets or sets the updated timestamp (server-owned).</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Looks up a node by id~ 🔍.</summary>
    /// <param name="id">The node id.</param>
    /// <returns>The node, or null.</returns>
    public DesignerNode? FindNode(string id) => this.Nodes.FirstOrDefault(n => n.Id == id);

    /// <summary>Raises <see cref="Changed"/> — called by commands after mutating~ 🔔.</summary>
    public void NotifyChanged() => this.Changed?.Invoke();

    /// <summary>Builds a document from a wire DTO, resolving each node's schema~ 🏗️.</summary>
    /// <param name="dto">The workflow DTO.</param>
    /// <param name="schemaResolver">Resolves a module id to its schema (null when unknown).</param>
    /// <returns>The document.</returns>
    public static DesignerDocument FromDto(WorkflowDto dto, Func<string, ModuleSchemaDto?> schemaResolver)
    {
        var doc = new DesignerDocument
        {
            Id = dto.Id,
            Name = dto.Name,
            Description = dto.Description,
            Version = dto.Version,
            Trigger = dto.Trigger,
            ErrorHandling = dto.ErrorHandling,
            CreatedAt = dto.CreatedAt,
            UpdatedAt = dto.UpdatedAt,
        };

        foreach (var n in dto.Nodes)
        {
            doc.Nodes.Add(DesignerNode.FromDto(n, schemaResolver(n.ModuleId)));
        }

        foreach (var c in dto.Connections)
        {
            doc.Connections.Add(DesignerConnection.FromDto(c));
        }

        if (dto.Variables is not null)
        {
            foreach (var (k, v) in dto.Variables)
            {
                doc.Variables[k] = v;
            }
        }

        if (dto.Tags is not null)
        {
            doc.Tags.AddRange(dto.Tags);
        }

        return doc;
    }

    /// <summary>Projects the document back to a wire DTO for save~ 📤.</summary>
    /// <returns>The workflow DTO.</returns>
    public WorkflowDto ToDto()
        => new(
            this.Id,
            this.Name,
            this.Description,
            this.Version,
            this.Nodes.Select(n => n.ToDto()).ToList(),
            this.Connections.Select(c => c.ToDto()).ToList(),
            this.Variables.Count > 0 ? new Dictionary<string, JsonElement>(this.Variables) : new Dictionary<string, JsonElement>(),
            this.Trigger,
            this.ErrorHandling,
            this.CreatedAt,
            this.UpdatedAt,
            this.Tags.Count > 0 ? new List<string>(this.Tags) : new List<string>());

    /// <summary>Creates an empty document with a staged name (for <c>/designer/new</c>)~ ✨.</summary>
    /// <param name="name">The staged name.</param>
    /// <returns>A blank document.</returns>
    public static DesignerDocument NewBlank(string name)
        => new() { Id = Guid.Empty, Name = string.IsNullOrWhiteSpace(name) ? "Untitled workflow" : name, Version = "1.0.0" };
}
