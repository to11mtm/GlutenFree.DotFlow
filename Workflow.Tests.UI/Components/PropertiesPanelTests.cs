// <copyright file="PropertiesPanelTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Components;

using System.Collections.Generic;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Designer.State;
using Workflow.UI.Client.Designer.State.Commands;
using Xunit;

/// <summary>
/// 🧪 Phase 3.3.b.3 — Properties panel + editor matrix + JSON-value + Monaco-fallback specs~ ✨.
/// </summary>
public sealed class PropertiesPanelTests : TestContext
{
    public PropertiesPanelTests() => this.JSInterop.Mode = JSRuntimeMode.Loose;

    private static JsonElement El(string j) => JsonDocument.Parse(j).RootElement.Clone();

    private static ModulePropertyDefinitionDto Prop(string name, string editor, bool required = false, List<JsonElement>? allowed = null)
        => new(name, name, "String", null, required, null, editor, allowed);

    private static (DesignerDocument Doc, SelectionState Sel, CommandStack Cmd) Setup(ModuleSchemaDto schema)
    {
        var doc = new DesignerDocument();
        var node = new DesignerNode { Id = "n1", ModuleId = "m", Name = "N1", Schema = schema };
        doc.Nodes.Add(node);
        var sel = new SelectionState();
        sel.SelectNode("n1");
        return (doc, sel, new CommandStack(doc));
    }

    private IRenderedComponent<PropertiesPanel> Render(DesignerDocument doc, SelectionState sel, CommandStack cmd)
        => this.RenderComponent<PropertiesPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, sel)
            .Add(x => x.Commands, cmd));

    [Theory]
    [InlineData("Text")]
    [InlineData("MultilineText")]
    [InlineData("Number")]
    [InlineData("Boolean")]
    [InlineData("Expression")]
    [InlineData("FilePath")]
    [InlineData("ConnectionString")]
    public void Panel_RendersEditor_PerEditorType(string editorType)
    {
        var schema = new ModuleSchemaDto(new(), new(), new List<ModulePropertyDefinitionDto> { Prop("p", editorType) });
        var (doc, sel, cmd) = Setup(schema);

        var cut = this.Render(doc, sel, cmd);

        cut.FindAll("[data-testid=editor-p]").Should().NotBeEmpty();
    }

    [Fact]
    public void Dropdown_UsesAllowedValues()
    {
        var schema = new ModuleSchemaDto(new(), new(), new List<ModulePropertyDefinitionDto>
        {
            Prop("method", "Dropdown", allowed: new List<JsonElement> { El("\"GET\""), El("\"POST\"") }),
        });
        var (doc, sel, cmd) = Setup(schema);

        var cut = this.Render(doc, sel, cmd);

        cut.Markup.Should().Contain("GET").And.Contain("POST");
    }

    [Fact]
    public void Required_EmptyShowsError()
    {
        var schema = new ModuleSchemaDto(new(), new(), new List<ModulePropertyDefinitionDto> { Prop("url", "Text", required: true) });
        var (doc, sel, cmd) = Setup(schema);

        var cut = this.Render(doc, sel, cmd);

        cut.FindAll("[data-testid=error-url]").Should().NotBeEmpty();
    }

    [Fact]
    public void Apply_ProducesSingleEditCommand_UndoRestores()
    {
        var schema = new ModuleSchemaDto(new(), new(), new List<ModulePropertyDefinitionDto> { Prop("url", "Text") });
        var (doc, sel, cmd) = Setup(schema);
        var cut = this.Render(doc, sel, cmd);

        cut.Find("[data-testid=editor-url]").Input("https://api.example.com");
        cut.Find("[data-testid=apply]").Click();

        doc.FindNode("n1")!.Properties["url"].GetString().Should().Be("https://api.example.com");

        cmd.Undo();
        doc.FindNode("n1")!.Properties.ContainsKey("url").Should().BeFalse();
    }

    [Fact]
    public void Rename_ViaHeader_UsesRenameCommand()
    {
        var schema = new ModuleSchemaDto(new(), new(), new List<ModulePropertyDefinitionDto>());
        var (doc, sel, cmd) = Setup(schema);
        var cut = this.Render(doc, sel, cmd);

        cut.Find("[data-testid=prop-name]").Change("My HTTP node");
        cut.Find("[data-testid=apply]").Click();

        doc.FindNode("n1")!.Name.Should().Be("My HTTP node");
    }

    [Fact]
    public void MultiSelect_ShowsSummaryOnly()
    {
        var doc = new DesignerDocument();
        doc.Nodes.Add(new DesignerNode { Id = "a", ModuleId = "m", Name = "A" });
        doc.Nodes.Add(new DesignerNode { Id = "b", ModuleId = "m", Name = "B" });
        var sel = new SelectionState();
        sel.SetNodes(new[] { "a", "b" });

        var cut = this.Render(doc, sel, new CommandStack(doc));

        cut.Markup.Should().Contain("2 nodes selected");
    }

    [Fact]
    public void NoSelection_ShowsWorkflowMeta_AndAppliesEdit()
    {
        var doc = new DesignerDocument { Name = "wf" };
        var sel = new SelectionState();
        var cmd = new CommandStack(doc);

        var cut = this.Render(doc, sel, cmd);
        cut.Find("[data-testid=wf-name]").Change("Renamed WF");
        cut.Find("[data-testid=wf-apply]").Click();

        doc.Name.Should().Be("Renamed WF");
    }

    [Fact]
    public void CodeEditor_MonacoLoadFails_UsesTextareaFallback()
    {
        // Loose JS interop returns default(false) for the Monaco create call → textarea stays.
        var cut = this.RenderComponent<CodeEditor>(p => p
            .Add(x => x.Value, "return 1;")
            .Add(x => x.Language, "javascript"));

        cut.FindAll("[data-testid=code-textarea]").Should().NotBeEmpty();
    }
}
