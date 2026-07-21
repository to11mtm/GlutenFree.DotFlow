// <copyright file="TemplateCatalogTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Scripts;

using System.Linq;
using FluentAssertions;
using Workflow.UI.Client.Scripts.State;
using Xunit;

/// <summary>
/// 📄 Phase 3.4.2 — Tests for the framework-free <see cref="ScriptTemplateCatalog"/>~ ✨.
/// </summary>
public sealed class TemplateCatalogTests
{
    [Fact]
    public void Catalog_HasAtLeastTenTemplates()
        => ScriptTemplateCatalog.Templates.Should().HaveCountGreaterThanOrEqualTo(10);

    [Fact]
    public void Catalog_FilterByLanguage_Works()
    {
        var js = ScriptTemplateCatalog.ForLanguage("javascript");
        js.Should().NotBeEmpty();
        js.Should().OnlyContain(t => t.Language == "javascript");

        ScriptTemplateCatalog.ForLanguage("lua").Should().OnlyContain(t => t.Language == "lua");
    }

    [Fact]
    public void Catalog_GroupByCategory_Works()
    {
        var groups = ScriptTemplateCatalog.ByCategory("javascript");
        groups.Should().NotBeEmpty();
        groups.Select(g => g.Category).Should().OnlyHaveUniqueItems();
        groups.SelectMany(g => g.Templates).Should().OnlyContain(t => t.Language == "javascript");
        groups.Should().Contain(g => g.Category == "HTTP");
    }

    [Fact]
    public void Catalog_EveryTemplate_HasCode()
        => ScriptTemplateCatalog.Templates.Should().OnlyContain(t => !string.IsNullOrWhiteSpace(t.Code) && !string.IsNullOrWhiteSpace(t.Name));
}
