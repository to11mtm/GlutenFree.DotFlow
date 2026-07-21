// <copyright file="PaginationBinding.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.V1;

using Workflow.Persistence.Models;

/// <summary>
/// 📑 Binds <c>?page=</c>/<c>?pageSize=</c> query params into a <see cref="Pagination"/> (Phase 2.7.0)~ ✨.
/// </summary>
public static class PaginationBinding
{
    /// <summary>
    /// Builds a <see cref="Pagination"/> from optional query values (clamped by the model)~ 📑.
    /// </summary>
    /// <param name="page">The 1-based page number, or <c>null</c> for page 1.</param>
    /// <param name="pageSize">The page size, or <c>null</c> for the default.</param>
    /// <returns>A <see cref="Pagination"/> (page ≥ 1, pageSize clamped to 1..200).</returns>
    public static Pagination From(int? page, int? pageSize)
        => new(page ?? 1, pageSize ?? Pagination.DefaultPageSize);
}
