// <copyright file="PaletteTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Components;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Tests.UI.Api;
using Workflow.UI.Client.Api;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Designer.State;
using Workflow.UI.Client.Designer.State.Commands;
using Workflow.UI.Client.Services;
using Xunit;

/// <summary>
/// 🧪 Phase 3.3.b.0 — Palette rendering/search + drag-to-create (AddNodeCommand) specs~ ✨.
/// </summary>
public sealed class PaletteTests : TestContext
{
    public PaletteTests() => this.JSInterop.Mode = JSRuntimeMode.Loose;

    private static string Json(object o) => JsonSerializer.Serialize(o, ApiHttp.Json);

    private void SetupModules(params ModuleSummaryDto[] modules)
    {
        var handler = FakeHttpMessageHandler.Json(Json(modules.ToList()));
        this.Services.AddSingleton(new ModulesClient(handler.CreateClient()));
        this.Services.AddSingleton(new PaletteDragState());
    }

    [Fact]
    public void Palette_RendersGroups_FromModules()
    {
        this.SetupModules(
            new("builtin.http.request", "HTTP Request", "HTTP", "d", "🌐", "1.0.0"),
            new("builtin.condition", "Condition", "Control Flow", "d", "◇", "1.0.0"));

        var cut = this.RenderComponent<ModulePalette>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("HTTP").And.Contain("Control Flow"));
        cut.Markup.Should().Contain("HTTP Request").And.Contain("Condition");
    }

    [Fact]
    public void Palette_Search_FiltersEntries()
    {
        this.SetupModules(
            new("builtin.http.request", "HTTP Request", "HTTP", "makes requests", "🌐", "1.0.0"),
            new("builtin.log", "Log", "Core", "logs stuff", "📝", "1.0.0"));

        var cut = this.RenderComponent<ModulePalette>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("HTTP Request"));

        cut.Find(".df-palette__search input").Input("log");

        cut.Markup.Should().Contain("Log");
        cut.Markup.Should().NotContain("HTTP Request");
    }

    [Fact]
    public void AddNodeCommand_Do_AddsNodeWithDefaults()
    {
        var doc = new DesignerDocument();
        var node = new DesignerNode { Id = "request-1", ModuleId = "builtin.http.request", Name = "request" };
        node.Properties["method"] = JsonDocument.Parse("\"GET\"").RootElement.Clone();

        new AddNodeCommand(node).Do(doc);

        doc.Nodes.Should().ContainSingle(n => n.Id == "request-1");
        doc.FindNode("request-1")!.Properties["method"].GetString().Should().Be("GET");
    }

    [Fact]
    public void AddNode_GeneratesUniqueId_AcrossRepeats()
    {
        var existing = new HashSet<string>();
        var a = NodeIdGenerator.Generate("builtin.http.request", existing);
        existing.Add(a);
        var b = NodeIdGenerator.Generate("builtin.http.request", existing);

        a.Should().Be("request-1");
        b.Should().Be("request-2");
    }

    [Fact]
    public void DragState_BeginEnd_RoundTrips()
    {
        var state = new PaletteDragState();
        state.Begin("builtin.log");
        state.DraggingModuleId.Should().Be("builtin.log");

        state.End().Should().Be("builtin.log");
        state.DraggingModuleId.Should().BeNull();
    }
}
