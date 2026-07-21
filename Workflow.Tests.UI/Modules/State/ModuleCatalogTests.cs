// <copyright file="ModuleCatalogTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Modules.State;

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Modules.State;
using Xunit;

/// <summary>
/// 📦 Phase 3.6.0 — Tests for the framework-free <see cref="ModuleCatalog"/>~ ✨.
/// </summary>
public sealed class ModuleCatalogTests
{
    private static ModuleSummaryDto M(string id, string name, string category, string desc, bool enabled = true)
        => new(id, name, category, desc, "🔧", "1.0.0", enabled);

    private static ModuleCatalog Catalog()
    {
        var c = new ModuleCatalog();
        c.SetModules(new[]
        {
            M("builtin.http.request", "HTTP Request", "HTTP", "Sends an HTTP request"),
            M("builtin.http.response", "HTTP Response", "HTTP", "Writes a response"),
            M("builtin.script", "Script", "Scripting", "Runs sandboxed code", enabled: false),
            M("builtin.log", "Log", "", "Logs a message"),
        });
        return c;
    }

    [Fact]
    public void Catalog_Search_FiltersByNameAndDescription()
    {
        var c = Catalog();
        c.Search = "sandboxed";
        c.Filtered().Should().ContainSingle(m => m.Id == "builtin.script");

        c.Search = "http";
        c.Filtered().Should().OnlyContain(m => m.Category == "HTTP");
    }

    [Fact]
    public void Catalog_Category_Filters()
    {
        var c = Catalog();
        c.Category = "HTTP";
        c.Filtered().Should().HaveCount(2);
        c.Category = "All";
        c.Filtered().Should().HaveCount(4);
    }

    [Fact]
    public void Catalog_EnabledOnly_Filters()
    {
        var c = Catalog();
        c.EnabledOnly = true;
        c.Filtered().Should().OnlyContain(m => m.Enabled);
        c.Filtered().Should().NotContain(m => m.Id == "builtin.script");
    }

    [Fact]
    public void Catalog_GroupByCategory_Orders_AndNormalizesBlank()
    {
        var c = Catalog();
        var groups = c.Grouped();

        groups.Select(g => g.Category).Should().ContainInOrder("HTTP", "Other", "Scripting");
        groups.First(g => g.Category == "Other").Modules.Should().ContainSingle(m => m.Id == "builtin.log");
        groups.First(g => g.Category == "HTTP").Modules.Select(m => m.DisplayName).Should().ContainInOrder("HTTP Request", "HTTP Response");
    }

    [Fact]
    public void Catalog_Categories_Distinct_Sorted()
        => Catalog().Categories.Should().ContainInOrder("HTTP", "Other", "Scripting");
}
