// <copyright file="Pagination.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Models;

/// <summary>
/// 📑 Pagination parameters for querying paged results~ ✨
/// PageSize is clamped to a maximum of <see cref="MaxPageSize"/> (200).
/// </summary>
public record Pagination
{
    /// <summary>Maximum allowed page size~ 🛑.</summary>
    public const int MaxPageSize = 200;

    /// <summary>Default page size~ 📄.</summary>
    public const int DefaultPageSize = 50;

    private readonly int _pageSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="Pagination"/> class~ 📑.
    /// </summary>
    /// <param name="page">The 1-based page number (defaults to 1).</param>
    /// <param name="pageSize">The number of items per page (defaults to 50, clamped to 200 max).</param>
    public Pagination(int page = 1, int pageSize = DefaultPageSize)
    {
        Page = Math.Max(1, page);
        _pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
    }

    /// <summary>Gets the 1-based page number~ 📄.</summary>
    public int Page { get; }

    /// <summary>Gets the number of items per page (clamped to max 200)~ 📊.</summary>
    public int PageSize => _pageSize;

    /// <summary>Gets the number of items to skip for the current page~ ⏭️.</summary>
    public int Skip => (Page - 1) * PageSize;

    /// <summary>Default pagination (page 1, 50 items)~ ✨.</summary>
    public static Pagination Default => new();
}

