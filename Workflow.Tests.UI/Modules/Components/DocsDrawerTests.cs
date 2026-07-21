// <copyright file="DocsDrawerTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Modules.Components;

using System.Collections.Generic;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Modules.Components;
using Workflow.UI.Client.Modules.State;
using Xunit;

/// <summary>
/// 📖 Phase 3.6.1 — bUnit tests for the generated documentation drawer~ ✨.
/// </summary>
public sealed class DocsDrawerTests : TestContext
{
    private static JsonElement El(string j) => JsonDocument.Parse(j).RootElement.Clone();

    private static ModuleDocModel Doc()
        => ModuleDocModel.From(new ModuleDetailsDto(
            "builtin.script", "Script", "Scripting", "Runs sandboxed code", "📜", "1.0.0",
            new ModuleSchemaDto(
                new List<PortDefinitionDto> { new("input", "Input", "object", "payload", true, null) },
                new List<PortDefinitionDto> { new("result", "Result", "any", null, false, null) },
                new List<ModulePropertyDefinitionDto>
                {
                    new("language", "Language", "string", null, true, El("\"javascript\""), "Select", new List<JsonElement> { El("\"javascript\""), El("\"lua\"") }),
                }),
            new List<string> { "builtin.log" }, true, new List<string> { "1.0.0", "0.9.0" }));

    [Fact]
    public void Drawer_RendersSchema_FromDetails()
    {
        var cut = this.RenderComponent<ModuleDocsDrawer>(p => p.Add(x => x.Doc, Doc()));

        cut.Find("[data-testid=drawer-name]").TextContent.Should().Be("Script");
        cut.Find("[data-testid=drawer-state]").TextContent.Should().Contain("enabled");
        cut.Find("[data-testid=input-input]").TextContent.Should().Contain("object");
        cut.Find("[data-testid=output-result]").Should().NotBeNull();
        cut.Find("[data-testid=prop-language]").TextContent.Should().Contain("Select");
        cut.Find("[data-testid=prop-language]").TextContent.Should().Contain("javascript");
    }

    [Fact]
    public void Drawer_ShowsVersionsAndDeps()
    {
        var cut = this.RenderComponent<ModuleDocsDrawer>(p => p.Add(x => x.Doc, Doc()));

        cut.Find("[data-testid='dep-builtin.log']").Should().NotBeNull();
        cut.Find("[data-testid='ver-1.0.0']").TextContent.Should().Contain("active");
        cut.Find("[data-testid='ver-0.9.0']").TextContent.Should().NotContain("active");
    }

    [Fact]
    public void Drawer_Close_RaisesCallback()
    {
        var closed = false;
        var cut = this.RenderComponent<ModuleDocsDrawer>(p => p
            .Add(x => x.Doc, Doc())
            .Add(x => x.OnClose, () => closed = true));

        cut.Find("[data-testid=drawer-close]").Click();

        closed.Should().BeTrue();
    }
}
