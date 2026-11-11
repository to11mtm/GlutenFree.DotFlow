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
    /// <summary>🔒 Plan 08 — the mask shown instead of a secret's value.</summary>
    public const string Mask = "••••••";

    /// <summary>🔒 Plan 08 — gets a value indicating whether the variable is secret (its value is never returned).</summary>
    public bool IsSecret { get; init; }

    /// <summary>🔒 Plan 08 — gets the secret mode: <c>None</c>, <c>Secret</c> or <c>FollowWorkflows</c>.</summary>
    public string SecretMode { get; init; } = "None";

    /// <summary>🔒 Plan 08 — gets who last changed a secret (from its trail), or null.</summary>
    public string? LastSetBy { get; init; }

    /// <summary>🔒 Plan 08 — gets when a secret was last changed, or null.</summary>
    public DateTimeOffset? LastSetAt { get; init; }

    /// <summary>🔒 Plan 08 — for a secret's history entry: what happened (no value).</summary>
    public string? Change { get; init; }

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
/// <summary>🔒 Plan 08 — a secret-mode change: the mode, and a value (required when a secret becomes Not secret).</summary>
/// <param name="Mode"><c>None</c>, <c>Secret</c> or <c>FollowWorkflows</c>.</param>
/// <param name="Value">An optional value to store with the change.</param>
public sealed record SecretModeRequest(string Mode, JsonElement? Value = null);

/// <summary>🔒 Plan 08 — "which stored variables would saving this workflow turn secret?"</summary>
/// <param name="WorkflowId">The workflow.</param>
/// <param name="SecretNames">The variables it declares secret.</param>
public sealed record SecretImpactRequest(Guid WorkflowId, List<string>? SecretNames);

public sealed record SetVariableRequest(JsonElement? Value)
{
    /// <summary>🔒 Plan 08 — optionally sets the secret mode in the same call (<c>None</c> / <c>Secret</c> / <c>FollowWorkflows</c>).</summary>
    public string? SecretMode { get; init; }

    /// <summary>Normalizes the JSON value into a CLR value for the store~ 🔄.</summary>
    /// <returns>The CLR value, or <c>null</c>.</returns>
    public object? ToClrValue()
        => this.Value is { } je && je.ValueKind != JsonValueKind.Null
            ? JsonValueConverter.FromElement(je)
            : null;
}
