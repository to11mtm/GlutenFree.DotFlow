// <copyright file="TemplatePickerTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Scripts.Components;

using System.Linq;
using Bunit;
using FluentAssertions;
using Workflow.UI.Client.Scripts.Components;
using Xunit;

/// <summary>
/// 📄 Phase 3.4.2 — bUnit tests for the template picker (insert vs replace + language filtering)~ ✨.
/// </summary>
public sealed class TemplatePickerTests : TestContext
{
    public TemplatePickerTests() => this.JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void Picker_Insert_IntoEmptyEditor_Replaces()
    {
        string? replaced = null;
        string? inserted = null;
        var cut = this.RenderComponent<TemplatePicker>(p => p
            .Add(x => x.Language, "javascript")
            .Add(x => x.CurrentCode, string.Empty)
            .Add(x => x.OnReplace, c => replaced = c)
            .Add(x => x.OnInsertAtCursor, c => inserted = c));

        cut.Find("[data-testid=templates-btn]").Click();
        cut.FindAll("[data-testid=template-item]").First().Click();

        replaced.Should().NotBeNull();
        inserted.Should().BeNull();
    }

    [Fact]
    public void Picker_Insert_IntoNonEmpty_ConfirmsThenInserts()
    {
        string? replaced = null;
        string? inserted = null;
        var cut = this.RenderComponent<TemplatePicker>(p => p
            .Add(x => x.Language, "javascript")
            .Add(x => x.CurrentCode, "const existing = 1;")
            .Add(x => x.OnReplace, c => replaced = c)
            .Add(x => x.OnInsertAtCursor, c => inserted = c));

        cut.Find("[data-testid=templates-btn]").Click();
        cut.FindAll("[data-testid=template-item]").First().Click();

        // A non-empty editor prompts before inserting.
        cut.Find("[data-testid=templates-confirm]").Should().NotBeNull();
        replaced.Should().BeNull();

        cut.Find("[data-testid=confirm-insert]").Click();
        inserted.Should().NotBeNull();
        replaced.Should().BeNull();
    }

    [Fact]
    public void Picker_ConfirmReplace_ReplacesAll()
    {
        string? replaced = null;
        var cut = this.RenderComponent<TemplatePicker>(p => p
            .Add(x => x.Language, "javascript")
            .Add(x => x.CurrentCode, "x")
            .Add(x => x.OnReplace, c => replaced = c));

        cut.Find("[data-testid=templates-btn]").Click();
        cut.FindAll("[data-testid=template-item]").First().Click();
        cut.Find("[data-testid=confirm-replace]").Click();

        replaced.Should().NotBeNull();
    }

    [Fact]
    public void Picker_FiltersToCurrentLanguage()
    {
        var cut = this.RenderComponent<TemplatePicker>(p => p.Add(x => x.Language, "lua"));

        cut.Find("[data-testid=templates-btn]").Click();

        var names = cut.FindAll("[data-testid=template-item]").Select(e => e.TextContent).ToList();
        names.Should().NotBeEmpty();
        names.Should().Contain(n => n.Contains("Lua"));
        // Toggling "all languages" surfaces JavaScript templates too.
        cut.Find("[data-testid=templates-all-langs]").Change(true);
        cut.Markup.Should().Contain("javascript");
    }
}
