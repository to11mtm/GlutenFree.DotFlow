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
    DateTimeOffset UpdatedAt);

/// <summary>🔧 Body for <c>PUT /api/v1/variables/{name}</c>~ 💾.</summary>
/// <param name="Value">The value to store; a JSON null persists a present null-valued entry.</param>
public sealed record SetVariableRequest(JsonElement? Value);
