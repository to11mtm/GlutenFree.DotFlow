// <copyright file="VariableDtos.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Api.Dtos;

using System;
using System.Text.Json;

/// <summary>
/// 🔧 Phase 3.5 (V6) — one versioned entry from the variable store
/// (<c>GET /api/v1/variables/{name}</c>)~ ✨.
/// </summary>
/// <param name="Name">The variable name.</param>
/// <param name="Value">The value as JSON (a JSON null means "present, but null").</param>
/// <param name="ValueTypeName">The stored CLR type name.</param>
/// <param name="Version">The version number of this entry.</param>
/// <param name="Scope">The scope kind: <c>Global</c>, <c>Workflow</c> or <c>Execution</c>.</param>
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
    /// <summary>🔒 Plan 08 — gets a value indicating whether the variable is secret (its value is never returned).</summary>
    public bool IsSecret { get; init; }

    /// <summary>🔒 Plan 08 — gets the secret mode: <c>None</c>, <c>Secret</c> or <c>FollowWorkflows</c>.</summary>
    public string SecretMode { get; init; } = "None";

    /// <summary>🔒 Plan 08 — gets who last changed a secret.</summary>
    public string? LastSetBy { get; init; }

    /// <summary>🔒 Plan 08 — gets when a secret was last changed.</summary>
    public DateTimeOffset? LastSetAt { get; init; }

    /// <summary>🔒 Plan 08 — for a secret's history entry: what happened (no value).</summary>
    public string? Change { get; init; }
}

/// <summary>🔒 Plan 08 — a stored variable that saving a workflow would turn secret.</summary>
/// <param name="Name">The variable.</param>
/// <param name="Scope"><c>Global</c> or <c>Workflow</c>.</param>
/// <param name="ScopeId">The workflow, for workflow scope.</param>
public sealed record SecretImpactDto(string Name, string Scope, Guid? ScopeId);

/// <summary>🔧 Body for <c>PUT /api/v1/variables/{name}</c>~ 💾.</summary>
/// <param name="Value">The value to store; a JSON null persists a present null-valued entry.</param>
public sealed record SetVariableRequest(JsonElement? Value);
