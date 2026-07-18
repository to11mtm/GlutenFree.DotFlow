// <copyright file="VariableContracts.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Contracts;

using System;
using System.Text.Json;
using Workflow.Modules.Internal;
using Workflow.Persistence.Models;

/// <summary>
/// 🔧 Phase 2.7.4 — Serializable projection of a <see cref="VariableEntry"/>~ ✨.
/// </summary>
/// <param name="Name">The variable name.</param>
/// <param name="Value">The value, as JSON (may be a JSON null for a present-but-null entry).</param>
/// <param name="ValueTypeName">The stored CLR type name of the value.</param>
/// <param name="Version">The version number of this entry.</param>
/// <param name="Scope">The scope kind (global/workflow/execution).</param>
/// <param name="ScopeId">The owning workflow/execution id, when scoped.</param>
/// <param name="CreatedAt">When this version was first created.</param>
/// <param name="UpdatedAt">When this version was written.</param>
public sealed record VariableDto(
    string Name,
    JsonElement? Value,
    string ValueTypeName,
    int Version,
    string Scope,
    Guid? ScopeId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Projects a <see cref="VariableEntry"/> into its DTO~ 🔧.</summary>
    /// <param name="entry">The store entry.</param>
    /// <returns>A serializable <see cref="VariableDto"/>.</returns>
    public static VariableDto From(VariableEntry entry)
        => new(
            entry.Name,
            ToElement(entry.Value),
            entry.ValueTypeName,
            entry.Version,
            entry.Scope.Kind.ToString(),
            entry.Scope.WorkflowId ?? entry.Scope.ExecutionId,
            entry.CreatedAt,
            entry.UpdatedAt);

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private static JsonElement? ToElement(object? value)
    {
        // A present-but-null variable value serializes to a JSON null element (not omitted)~
        if (value is null)
        {
            using var doc = JsonDocument.Parse("null");
            return doc.RootElement.Clone();
        }

        try
        {
            return JsonSerializer.SerializeToElement(value, Options);
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(value.ToString(), Options);
        }
    }
}

/// <summary>
/// 🔧 Phase 2.7.4 — Request body for setting a variable value (creates a new version)~ ✨.
/// </summary>
/// <param name="Value">The value to store; a JSON null persists a present null-valued entry.</param>
public sealed record SetVariableRequest(JsonElement? Value)
{
    /// <summary>Normalizes the JSON value into a CLR value for the store~ 🔄.</summary>
    /// <returns>The CLR value, or <c>null</c>.</returns>
    public object? ToClrValue()
        => this.Value is { } je && je.ValueKind != JsonValueKind.Null
            ? JsonValueConverter.FromElement(je)
            : null;
}
