// <copyright file="VariableClashPanelTests.cs" company="GlutenFree">
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
/// §10.2 — the properties panel explains a <c>{{Variable.x}}</c> / wired-port-<c>x</c> clash and
/// offers three fixes: use the incoming value, keep the variable, or rename the variable.
/// </summary>
public sealed class VariableClashPanelTests : TestContext
{
    public VariableClashPanelTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new Workflow.UI.Client.Scripts.State.ScriptStudioHandoff());
        this.Services.AddSingleton(new Workflow.UI.Client.Linq.State.LinqStudioHandoff());
    }

    private static JsonElement El(string j) => JsonDocument.Parse(j).RootElement.Clone();

    private (IRenderedComponent<PropertiesPanel> Cut, DesignerDocument Doc, CommandStack Cmd) Render(bool declareCount)
    {
        var doc = new DesignerDocument();
        doc.Nodes.Add(new DesignerNode { Id = "src", ModuleId = "m.src", Name = "Source" });
        doc.Nodes.Add(new DesignerNode
        {
            Id = "sink",
            ModuleId = "m.sink",
            Name = "Sink",
            Schema = new ModuleSchemaDto(new(), new(), new List<ModulePropertyDefinitionDto>
            {
                new("url", "url", "String", null, false, null, "Text", null, true),
            }),
            Properties = { ["url"] = El("\"/items/{{Variable.count}}\"") },
        });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "src", SourcePortName = "value", TargetNodeId = "sink", TargetPortName = "count" });
        if (declareCount)
        {
            doc.Variables["count"] = El("""{ "name": "count", "type": 1, "initialValue": 99 }""");
        }

        var sel = new SelectionState();
        sel.SelectNode("sink");
        var cmd = new CommandStack(doc);
        var cut = this.RenderComponent<PropertiesPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, sel)
            .Add(x => x.Commands, cmd));
        return (cut, doc, cmd);
    }

    [Fact]
    public void Clash_IsExplained_WithAllThreeFixes_WhenTheVariableIsDeclared()
    {
        var (cut, _, _) = this.Render(declareCount: true);

        cut.Find("[data-testid=clash-url-count]").TextContent.Should().Contain("reads the variable 'count'").And.Contain("{{src.value}}");
        cut.FindAll("[data-testid=clash-use-incoming-url-count]").Should().ContainSingle();
        cut.FindAll("[data-testid=clash-keep-url-count]").Should().ContainSingle();
        cut.FindAll("[data-testid=clash-rename-url-count]").Should().ContainSingle();
    }

    [Fact]
    public void WithNoVariable_OnlyUseTheIncomingValueIsOffered()
    {
        var (cut, _, _) = this.Render(declareCount: false);

        cut.Find("[data-testid=clash-url-count]").TextContent.Should().Contain("there's no variable 'count'");
        cut.FindAll("[data-testid=clash-use-incoming-url-count]").Should().ContainSingle();
        cut.FindAll("[data-testid=clash-keep-url-count]").Should().BeEmpty(because: "there's no variable to keep");
        cut.FindAll("[data-testid=clash-rename-url-count]").Should().BeEmpty(because: "there's nothing declared to rename");
    }

    [Fact]
    public void UseTheIncomingValue_RewritesTheField()
    {
        var (cut, _, _) = this.Render(declareCount: true);

        cut.Find("[data-testid=clash-use-incoming-url-count]").Click();

        cut.Find("[data-testid=editor-url]").GetAttribute("value").Should().Be("/items/{{src.value}}");
        cut.FindAll("[data-testid=clash-url-count]").Should().BeEmpty();
    }

    [Fact]
    public void KeepTheVariable_RecordsTheChoiceOnTheNode_Undoably()
    {
        var (cut, doc, cmd) = this.Render(declareCount: true);

        cut.Find("[data-testid=clash-keep-url-count]").Click();

        doc.FindNode("sink")!.Metadata[VariableLint.ClashAckKey].Should().Be("count@src.value");
        cut.FindAll("[data-testid=clash-url-count]").Should().BeEmpty();

        cmd.Undo();
        doc.FindNode("sink")!.Metadata.Should().NotContainKey(VariableLint.ClashAckKey);
    }

    [Fact]
    public void RenameTheVariable_RenamesItAndTheField()
    {
        var (cut, doc, _) = this.Render(declareCount: true);

        cut.Find("[data-testid=clash-rename-url-count]").Click();
        cut.Find("[data-testid=clash-rename-input-url-count]").Input("pageSize");
        cut.Find("[data-testid=clash-rename-apply-url-count]").Click();

        doc.Variables.Should().ContainKey("pageSize").And.NotContainKey("count");
        doc.FindNode("sink")!.Properties["url"].GetString().Should().Be("/items/{{Variable.pageSize}}");
        cut.Find("[data-testid=editor-url]").GetAttribute("value").Should().Be("/items/{{Variable.pageSize}}",
            because: "the panel's unapplied edit buffer follows the rename");
        cut.FindAll("[data-testid=clash-url-count]").Should().BeEmpty();
    }

    [Fact]
    public void RenameTheVariable_RejectsAnInvalidName()
    {
        var (cut, doc, _) = this.Render(declareCount: true);

        cut.Find("[data-testid=clash-rename-url-count]").Click();
        cut.Find("[data-testid=clash-rename-input-url-count]").Input("not a name!");
        cut.Find("[data-testid=clash-rename-apply-url-count]").Click();

        cut.Find("[data-testid=clash-rename-error-url-count]").Should().NotBeNull();
        doc.Variables.Should().ContainKey("count");
    }
}
