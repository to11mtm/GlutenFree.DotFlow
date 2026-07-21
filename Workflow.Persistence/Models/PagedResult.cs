// <copyright file="PagedResult.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Models;

/// <summary>
/// 📄 A paginated result set with total count and navigation helpers~ ✨
/// </summary>
/// <typeparam name="T">The type of items in the result set.</typeparam>
public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    /// <summary>Gets whether there is a next page available~ ➡️.</summary>
    public bool HasNextPage => Page * PageSize < TotalCount;

    /// <summary>Gets whether there is a previous page available~ ⬅️.</summary>
    public bool HasPreviousPage => Page > 1;

    /// <summary>Gets the total number of pages~ 📊.</summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;

    /// <summary>Creates an empty paged result~ 📭.</summary>
    public static PagedResult<T> Empty(int page = 1, int pageSize = 50) =>
        new(Array.Empty<T>(), 0, page, pageSize);
}

