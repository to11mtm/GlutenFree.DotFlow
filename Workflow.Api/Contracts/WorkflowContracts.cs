// <copyright file="WorkflowContracts.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Contracts;

using System;
using System.Collections.Generic;
using System.Linq;
using Workflow.Core.Models;

/// <summary>
/// 📋 Lightweight list-view projection of a <see cref="WorkflowDefinition"/> (Phase 2.7.1)~ ✨.
/// </summary>
/// <param name="Id">The workflow id.</param>
/// <param name="Name">The workflow name.</param>
/// <param name="Description">The description.</param>
/// <param name="Version">The version (string).</param>
/// <param name="Tags">The tags.</param>
/// <param name="NodeCount">Number of nodes.</param>
/// <param name="CreatedAt">Created timestamp.</param>
/// <param name="UpdatedAt">Updated timestamp.</param>
public record WorkflowSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    string Version,
    IReadOnlyList<string> Tags,
    int NodeCount,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt)
{
    /// <summary>
    /// Projects a <see cref="WorkflowDefinition"/> into a summary DTO~ 📋.
    /// </summary>
    /// <param name="d">The definition.</param>
    /// <returns>The summary DTO.</returns>
    public static WorkflowSummaryDto From(WorkflowDefinition d)
        => new(
            d.Id,
            d.Name,
            d.Description,
            d.Version.ToString(),
            d.Tags.HasValue ? d.Tags.Value.ToList() : new List<string>(),
            d.Nodes.Count,
            d.CreatedAt,
            d.UpdatedAt);
}

/// <summary>
/// 📄 A paginated page envelope for API list responses (Phase 2.7.1)~ ✨.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">The page items.</param>
/// <param name="TotalCount">Total matching items.</param>
/// <param name="Page">The 1-based page.</param>
/// <param name="PageSize">The page size.</param>
/// <param name="TotalPages">Total pages.</param>
public record PageDto<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages);

/// <summary>
/// ✅ Phase 3.3 (D14) — Result of the dry-run workflow validate endpoint~ ✨.
/// </summary>
/// <param name="Valid">Whether the workflow passed validation.</param>
/// <param name="Issues">The issues found (empty when valid).</param>
public record WorkflowValidationResultDto(bool Valid, IReadOnlyList<WorkflowValidationIssueDto> Issues);

/// <summary>✅ Phase 3.3 (D14) — A single validation issue~ ✨.</summary>
/// <param name="Severity"><c>error</c> or <c>warning</c>.</param>
/// <param name="Message">Human-readable message.</param>
/// <param name="NodeId">The offending node id, when applicable.</param>
public record WorkflowValidationIssueDto(string Severity, string Message, string? NodeId);
