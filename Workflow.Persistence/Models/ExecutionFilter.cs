// <copyright file="ExecutionFilter.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Models;

using Workflow.Core.Models;

/// <summary>
/// 🔍 Filter criteria for querying execution history~ ✨
/// </summary>
public record ExecutionFilter(
    ExecutionState[]? States = null,
    DateTimeOffset? StartedAfter = null,
    DateTimeOffset? StartedBefore = null)
{
    /// <summary>No-filter instance (returns all executions)~ 📊.</summary>
    public static ExecutionFilter None => new();
}

