// <copyright file="WorkflowFilter.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Models;

/// <summary>
/// 🔍 Filter criteria for querying workflows~ ✨
/// </summary>
public record WorkflowFilter(
    string? NameContains = null,
    bool? IsActive = null,
    string[]? Tags = null,
    DateTimeOffset? CreatedAfter = null,
    DateTimeOffset? CreatedBefore = null)
{
    /// <summary>No-filter instance (returns all active workflows)~ 📋.</summary>
    public static WorkflowFilter None => new();
}

