// <copyright file="SplitPreviewPanelTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Components;

using System.Collections.Generic;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// 🧪 Split preview V2/V3 — bUnit specs for the PropertiesPanel section: live rows, empty state,
/// missing-key warning, persist toggle + confirm-on-off (Q2), merged-upstream shape mode (Q1a)~ 🧩.
/// </summary>
public sealed class SplitPreviewPanelTests : TestContext
{
    public SplitPreviewPanelTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new Workflow.UI.Client.Scripts.State.ScriptStudioHandoff());
        this.Services.AddSingleton(new Workflow.UI.Client.Linq.State.LinqStudioHandoff());
    }

    private static JsonElement El(string j) => JsonDocument.Parse(j).RootElement.Clone();

    private static ModuleSchemaDto SplitSchema()
        => new(
            new List<PortDefinitionDto> { new("value", "value", "object", null, false, null) },
            new List<PortDefinitionDto>(),
            new List<ModulePropertyDefinitionDto>
            {
                new("keys", "keys", "String", null, true, null, "Json", null),
                new("restPort", "restPort", "String", null, false, null, "Text", null),
            });

    private static (DesignerDocument Doc, SelectionState Sel, CommandStack Cmd, DesignerNode Node) Setup()
    {
        var doc = new DesignerDocument();
        var node = new DesignerNode { Id = "split-1", ModuleId = "builtin.split", Name = "Split", Schema = SplitSchema() };
        node.Properties["keys"] = El("""["Foo", "Qux"]""");
        node.Properties["restPort"] = El("\"rest\"");
        doc.Nodes.Add(node);
        var sel = new SelectionState();
        sel.SelectNode("split-1");
        return (doc, sel, new CommandStack(doc), node);
    }

    private IRenderedComponent<PropertiesPanel> Render(DesignerDocument doc, SelectionState sel, CommandStack cmd)
        => this.RenderComponent<PropertiesPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, sel)
            .Add(x => x.Commands, cmd));

    [Fact]
    public void NonSplitNodes_HaveNoPreviewSection()
    {
        var doc = new DesignerDocument();
        doc.Nodes.Add(new DesignerNode { Id = "n", ModuleId = "m", Name = "n" });
        var sel = new SelectionState();
        sel.SelectNode("n");

        var cut = this.Render(doc, sel, new CommandStack(doc));

        cut.FindAll("[data-testid=split-preview]").Should().BeEmpty();
    }

    [Fact]
    public void EmptySample_ShowsThePasteHint()
    {
        var (doc, sel, cmd, _) = Setup();

        var cut = this.Render(doc, sel, cmd);

        cut.Find("[data-testid=split-preview-empty]").TextContent.Should().Contain("sample");
    }

    [Fact]
    public void PastedSample_ShowsRowsWithMissingWarningAndRestBucket()
    {
        var (doc, sel, cmd, _) = Setup();
        var cut = this.Render(doc, sel, cmd);

        cut.Find("[data-testid=split-sample]").Change("""{ "Foo": 1, "Baz": 3 }""");

        cut.Find("[data-testid=split-row-Foo]").TextContent.Should().Contain("1");
        cut.Find("[data-testid=split-row-Qux]").TextContent.Should().Contain("not in sample");
        cut.Find("[data-testid=split-row-rest]").TextContent.Should().Contain("Baz");
    }

    [Fact]
    public void PersistToggleOn_StoresTheSampleAsMetadata_Undoably()
    {
        var (doc, sel, cmd, node) = Setup();
        var cut = this.Render(doc, sel, cmd);
        cut.Find("[data-testid=split-sample]").Change("""{ "Foo": 1 }""");

        cut.Find("[data-testid=split-persist]").Change(true);

        SplitPreview.PersistedSample(node).Should().Be("""{ "Foo": 1 }""");
        cmd.Undo();
        SplitPreview.PersistedSample(node).Should().BeNull();
    }

    [Fact]
    public void PersistToggleOff_WithStoredSample_AsksFirst_RemovePrimary()
    {
        var (doc, sel, cmd, node) = Setup();
        node.Metadata[SplitPreview.SampleMetadataKey] = """{ "Foo": 1 }""";
        var cut = this.Render(doc, sel, cmd);

        cut.Find("[data-testid=split-persist]").Change(false);

        // Nothing removed yet — the confirm decides (Q2 follow-up).
        SplitPreview.PersistedSample(node).Should().NotBeNull();
        cut.Find("[data-testid=split-persist-confirm]").TextContent.Should().Contain("stored");

        cut.Find("[data-testid=split-persist-remove]").Click();
        SplitPreview.PersistedSample(node).Should().BeNull();
    }

    [Fact]
    public void PersistToggleOff_KeepChoice_LeavesTheStoredSample()
    {
        var (doc, sel, cmd, node) = Setup();
        node.Metadata[SplitPreview.SampleMetadataKey] = """{ "Foo": 1 }""";
        var cut = this.Render(doc, sel, cmd);

        cut.Find("[data-testid=split-persist]").Change(false);
        cut.Find("[data-testid=split-persist-keep]").Click();

        SplitPreview.PersistedSample(node).Should().Be("""{ "Foo": 1 }""");
        cut.FindAll("[data-testid=split-persist-confirm]").Should().BeEmpty();
    }

    [Fact]
    public void PersistedSample_SeedsTheEditorOnSelection()
    {
        var (doc, sel, cmd, node) = Setup();
        node.Metadata[SplitPreview.SampleMetadataKey] = """{ "Foo": 42 }""";

        var cut = this.Render(doc, sel, cmd);

        cut.Find("[data-testid=split-row-Foo]").TextContent.Should().Contain("42");
        cut.Find("[data-testid=split-persist]").GetAttribute("checked").Should().NotBeNull();
    }

    [Fact]
    public void MergedUpstream_UsesShapeMode_NoSampleNeeded()
    {
        var (doc, sel, cmd, _) = Setup();
        var http = new DesignerNode
        {
            Id = "http-1",
            ModuleId = "builtin.http.request",
            Name = "HTTP",
            Schema = new ModuleSchemaDto(
                new List<PortDefinitionDto>(),
                new List<PortDefinitionDto>
                {
                    new("Foo", "Foo", "int", null, false, null),
                    new("body", "body", "string", null, false, null),
                },
                new List<ModulePropertyDefinitionDto>()),
        };
        http.Properties[OutputShapingUx.PropertyName] = El("\"merged\"");
        doc.Nodes.Add(http);
        doc.Connections.Add(new DesignerConnection
        {
            SourceNodeId = "http-1", SourcePortName = "output", TargetNodeId = "split-1", TargetPortName = "value",
        });

        var cut = this.Render(doc, sel, cmd);

        cut.Find("[data-testid=split-shape-source]").TextContent.Should().Contain("no sample needed");
        cut.Find("[data-testid=split-row-Foo]").TextContent.Should().Contain("value from upstream");
        cut.Find("[data-testid=split-row-Qux]").TextContent.Should().Contain("not present");
        cut.Find("[data-testid=split-row-rest]").TextContent.Should().Contain("body");
        cut.FindAll("[data-testid=split-sample]").Should().BeEmpty();
    }
}
