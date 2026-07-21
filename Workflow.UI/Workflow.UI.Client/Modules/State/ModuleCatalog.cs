// <copyright file="ModuleCatalog.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Modules.State;

using System;
using System.Collections.Generic;
using System.Linq;
using Workflow.UI.Client.Api.Dtos;

/// <summary>
/// 📦 Phase 3.6.0 — Framework-free filtering + category grouping for the Module Manager grid
/// (generalizes the designer palette's grouping). No Blazor/JS types (D2)~ ✨.
/// </summary>
public sealed class ModuleCatalog
{
    private readonly List<ModuleSummaryDto> modules = new();

    /// <summary>Gets or sets the search text (id / name / description).</summary>
    public string Search { get; set; } = string.Empty;

    /// <summary>Gets or sets the category filter (null / "All" = every category).</summary>
    public string? Category { get; set; }

    /// <summary>Gets or sets a value indicating whether to show only enabled modules.</summary>
    public bool EnabledOnly { get; set; }

    /// <summary>Replaces the backing module set~ 🔄.</summary>
    /// <param name="items">The modules.</param>
    public void SetModules(IEnumerable<ModuleSummaryDto> items)
    {
        this.modules.Clear();
        this.modules.AddRange(items);
    }

    /// <summary>Gets the distinct category names (sorted)~ 🗂️.</summary>
    public IReadOnlyList<string> Categories
        => this.modules.Select(m => NormalizeCategory(m.Category)).Distinct().OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Gets the filtered modules (flat, ordered)~ 🔍.</summary>
    public IReadOnlyList<ModuleSummaryDto> Filtered()
    {
        IEnumerable<ModuleSummaryDto> q = this.modules;

        if (!string.IsNullOrWhiteSpace(this.Search))
        {
            var f = this.Search.Trim();
            q = q.Where(m =>
                m.Id.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                m.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                m.Description.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(this.Category) && !string.Equals(this.Category, "All", StringComparison.OrdinalIgnoreCase))
        {
            q = q.Where(m => string.Equals(NormalizeCategory(m.Category), this.Category, StringComparison.OrdinalIgnoreCase));
        }

        if (this.EnabledOnly)
        {
            q = q.Where(m => m.Enabled);
        }

        return q.OrderBy(m => NormalizeCategory(m.Category), StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Groups the filtered modules by category (in category order)~ 🗂️.</summary>
    /// <returns>Category → modules.</returns>
    public IReadOnlyList<(string Category, IReadOnlyList<ModuleSummaryDto> Modules)> Grouped()
        => this.Filtered()
            .GroupBy(m => NormalizeCategory(m.Category))
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, (IReadOnlyList<ModuleSummaryDto>)g.OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();

    private static string NormalizeCategory(string? category)
        => string.IsNullOrWhiteSpace(category) ? "Other" : category;
}
