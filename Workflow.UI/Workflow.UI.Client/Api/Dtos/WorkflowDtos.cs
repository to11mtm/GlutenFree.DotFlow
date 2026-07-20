// <copyright file="WorkflowDtos.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Api.Dtos;

using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// 📋 Phase 3.3.a.0 — Plain, framework-free mirror of the workflow wire contracts (D2/D5).
/// These records match the JSON the API emits from <c>GET /api/v1/workflows/{id}</c> (the raw
/// serialized <c>WorkflowDefinition</c>, LanguageExt collections rendered as objects/arrays) and
/// are exactly what a future TypeScript client would consume — no LanguageExt, no Blazor~ ✨.
/// </summary>
/// <param name="Id">The workflow id.</param>
/// <param name="Name">The workflow name.</param>
/// <param name="Description">Optional description.</param>
/// <param name="Version">Semantic version string (e.g. <c>1.4.0</c>).</param>
/// <param name="Nodes">The graph nodes.</param>
/// <param name="Connections">The graph edges.</param>
/// <param name="Variables">Workflow variables (name → raw definition JSON, passed through losslessly).</param>
/// <param name="Trigger">Optional trigger definition (passthrough — not edited in MVP).</param>
/// <param name="ErrorHandling">Optional workflow-level error handling (passthrough).</param>
/// <param name="CreatedAt">Created timestamp.</param>
/// <param name="UpdatedAt">Updated timestamp.</param>
/// <param name="Tags">Optional tags.</param>
public sealed record WorkflowDto(
    Guid Id,
    string Name,
    string? Description,
    string Version,
    List<NodeDto> Nodes,
    List<ConnectionDto> Connections,
    Dictionary<string, JsonElement>? Variables,
    JsonElement? Trigger,
    JsonElement? ErrorHandling,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    List<string>? Tags);

/// <summary>
/// 🎯 Phase 3.3.a.0 — A graph node (module instance). Properties are raw <see cref="JsonElement"/>
/// values so JSON types are preserved exactly on round-trip (a number stays a number)~ ✨.
/// </summary>
/// <param name="Id">The node id (unique within the workflow).</param>
/// <param name="ModuleId">The module this node instantiates.</param>
/// <param name="Name">The display name.</param>
/// <param name="Properties">Configured property values (name → JSON value).</param>
/// <param name="Position">Canvas position (persisted layout — D5).</param>
/// <param name="ErrorHandling">Optional per-node error handling (passthrough).</param>
/// <param name="Timeout">Optional per-node timeout (seconds).</param>
/// <param name="RetryPolicy">Optional retry policy (passthrough).</param>
/// <param name="Metadata">Optional metadata (e.g. <c>moduleVersion</c>, <c>ui.*</c>).</param>
/// <param name="RegionId">Optional region id.</param>
public sealed record NodeDto(
    string Id,
    string ModuleId,
    string Name,
    Dictionary<string, JsonElement> Properties,
    PositionDto? Position,
    JsonElement? ErrorHandling = null,
    int? Timeout = null,
    JsonElement? RetryPolicy = null,
    Dictionary<string, string>? Metadata = null,
    string? RegionId = null);

/// <summary>📍 Phase 3.3.a.0 — Canvas coordinates for a node~ ✨.</summary>
/// <param name="X">Horizontal position.</param>
/// <param name="Y">Vertical position.</param>
public sealed record PositionDto(double X, double Y);

/// <summary>
/// 🔗 Phase 3.3.a.0 — A directed connection between two node ports~ ✨.
/// </summary>
/// <param name="SourceNodeId">Source node id.</param>
/// <param name="SourcePortName">Source output port.</param>
/// <param name="TargetNodeId">Target node id.</param>
/// <param name="TargetPortName">Target input port.</param>
/// <param name="Condition">Optional runtime condition expression.</param>
/// <param name="Priority">Ordering priority (lower first).</param>
public sealed record ConnectionDto(
    string SourceNodeId,
    string SourcePortName,
    string TargetNodeId,
    string TargetPortName,
    string? Condition = null,
    int Priority = 0);

/// <summary>
/// 📋 Phase 3.3.a.0 — Lightweight workflow list row (mirrors <c>WorkflowSummaryDto</c>)~ ✨.
/// </summary>
/// <param name="Id">The workflow id.</param>
/// <param name="Name">The name.</param>
/// <param name="Description">Optional description.</param>
/// <param name="Version">Version string.</param>
/// <param name="Tags">Tags.</param>
/// <param name="NodeCount">Number of nodes.</param>
/// <param name="CreatedAt">Created timestamp.</param>
/// <param name="UpdatedAt">Updated timestamp.</param>
public sealed record WorkflowSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    string Version,
    List<string> Tags,
    int NodeCount,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// 📄 Phase 3.3.a.0 — Paged list envelope (mirrors the API's <c>PageDto&lt;T&gt;</c>)~ ✨.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">The page items.</param>
/// <param name="TotalCount">Total matching items.</param>
/// <param name="Page">1-based page.</param>
/// <param name="PageSize">Page size.</param>
/// <param name="TotalPages">Total pages.</param>
public sealed record PageDto<T>(
    List<T> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages);
