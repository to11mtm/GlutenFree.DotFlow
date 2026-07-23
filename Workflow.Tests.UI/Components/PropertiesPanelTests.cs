// <copyright file="PropertiesPanelTests.cs" company="GlutenFree">
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
using Workflow.UI.Client.Designer.State.Commands;
using Xunit;

/// <summary>
/// 🧪 Phase 3.3.b.3 — Properties panel + editor matrix + JSON-value + Monaco-fallback specs~ ✨.
/// </summary>
public sealed class PropertiesPanelTests : TestContext
{
    public PropertiesPanelTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new Workflow.UI.Client.Scripts.State.ScriptStudioHandoff());
    }

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
    public void Property_Description_RendersTooltip_AndDefault()
    {
        // G3: description + default become the label tooltip, with an ⓘ affordance.
        var prop = new ModulePropertyDefinitionDto("mode", "Mode", "String", "How to write the file", false, El("\"overwrite\""), "Text", null);
        var schema = new ModuleSchemaDto(new(), new(), new List<ModulePropertyDefinitionDto> { prop });
        var (doc, sel, cmd) = Setup(schema);

        var cut = this.Render(doc, sel, cmd);

        var info = cut.Find("[data-testid=info-mode]");
        info.GetAttribute("title").Should().Contain("How to write the file").And.Contain("Default: overwrite");
    }

    [Fact]
    public void Property_TallEditor_ShowsHelperLine()
    {
        var prop = new ModulePropertyDefinitionDto("body", "Body", "String", "Request payload template", false, null, "MultilineText", null);
        var schema = new ModuleSchemaDto(new(), new(), new List<ModulePropertyDefinitionDto> { prop });
        var (doc, sel, cmd) = Setup(schema);

        var cut = this.Render(doc, sel, cmd);

        cut.Find("[data-testid=help-body]").TextContent.Should().Contain("Request payload template");
    }

    [Fact]
    public void Property_NoDescription_NoInfoOrHelper()
    {
        var schema = new ModuleSchemaDto(new(), new(), new List<ModulePropertyDefinitionDto> { Prop("p", "MultilineText") });
        var (doc, sel, cmd) = Setup(schema);

        var cut = this.Render(doc, sel, cmd);

        cut.FindAll("[data-testid=info-p]").Should().BeEmpty();
        cut.FindAll("[data-testid=help-p]").Should().BeEmpty();
    }

    [Fact]
    public void TokenPicker_InsertsVariableToken_AndShowsBoundBadge()
    {
        // G4: workflow variable + an upstream node output are pickable.
        var schema = new ModuleSchemaDto(new(), new(), new List<ModulePropertyDefinitionDto> { Prop("url", "Text") });
        var (doc, sel, cmd) = Setup(schema);
        doc.Variables["baseUrl"] = El("\"https://example.com\"");
        doc.Nodes.Add(new DesignerNode { Id = "up1", ModuleId = "builtin.log", Name = "Upstream", X = 0, Y = 0 });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "up1", SourcePortName = "output", TargetNodeId = "n1", TargetPortName = "input" });

        var cut = this.Render(doc, sel, cmd);

        cut.Find("[data-testid=token-btn-url]").Click();
        var list = cut.Find("[data-testid=token-list-url]");
        list.TextContent.Should().Contain("baseUrl").And.Contain("Upstream · output");

        // Pick the variable → token inserted into the buffer, badge appears.
        cut.FindAll(".df-token-list__item").First(b => b.TextContent.Contains("baseUrl")).Click();
        cut.Find("[data-testid=editor-url]").GetAttribute("value").Should().Be("{{Variable.baseUrl}}");
        cut.FindAll("[data-testid=bound-url]").Should().ContainSingle();
    }

    [Fact]
    public void OutputsStrip_ListsPorts_AndReflectsMergedMode()
    {
        // G6: the strip shows current output ports and updates when output mode changes.
        var schema = new ModuleSchemaDto(
            new(),
            new List<PortDefinitionDto>
            {
                new("body", "Body", "object", "Response payload", false, null),
                new("statusCode", "Status", "int", null, false, null),
            },
            new List<ModulePropertyDefinitionDto>());
        var (doc, sel, cmd) = Setup(schema);

        var cut = this.Render(doc, sel, cmd);

        var strip = cut.Find("[data-testid=outputs-strip]");
        strip.TextContent.Should().Contain("body").And.Contain("statusCode");

        cut.Find("[data-testid=output-mode]").Change("merged");
        cut.Find("[data-testid=outputs-strip]").TextContent.Trim().Should().Be("output");
    }

    [Fact]
    public void Modal_OpensEditsApplies_AsOneCommand()
    {
        // G5: the expand modal shares the panel buffer; Apply commits via the same command path.
        var schema = new ModuleSchemaDto(new(), new(), new List<ModulePropertyDefinitionDto> { Prop("url", "Text") });
        var (doc, sel, cmd) = Setup(schema);

        var cut = this.Render(doc, sel, cmd);

        cut.Find("[data-testid=expand-editor]").Click();
        cut.Find("[data-testid=node-modal]").Should().NotBeNull();

        // Edit inside the modal (second rendered editor for the property), then apply.
        cut.FindAll("[data-testid=editor-url]")[^1].Input("https://example.com");
        cut.Find("[data-testid=modal-apply]").Click();

        cut.FindAll("[data-testid=node-modal]").Should().BeEmpty(because: "apply closes the modal~ 🪟");
        JsonValues.ToText(doc.FindNode("n1")!.Properties["url"]).Should().Be("https://example.com");
        cmd.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void Modal_Cancel_DiscardsBufferedEdits()
    {
        var schema = new ModuleSchemaDto(new(), new(), new List<ModulePropertyDefinitionDto> { Prop("url", "Text") });
        var (doc, sel, cmd) = Setup(schema);

        var cut = this.Render(doc, sel, cmd);
        cut.Find("[data-testid=expand-editor]").Click();
        cut.FindAll("[data-testid=editor-url]")[^1].Input("discard-me");
        cut.Find("[data-testid=modal-cancel]").Click();

        cut.FindAll("[data-testid=node-modal]").Should().BeEmpty();
        doc.FindNode("n1")!.Properties.Should().NotContainKey("url");
        // The panel buffer was re-seeded — the panel editor shows the original (empty) value.
        cut.Find("[data-testid=editor-url]").GetAttribute("value").Should().BeNullOrEmpty();
    }

    [Fact]
    public void TokenPicker_Hidden_WhenNoOptions()
    {
        var schema = new ModuleSchemaDto(new(), new(), new List<ModulePropertyDefinitionDto> { Prop("url", "Text") });
        var (doc, sel, cmd) = Setup(schema); // no variables, no upstream nodes

        var cut = this.Render(doc, sel, cmd);

        cut.FindAll("[data-testid=token-btn-url]").Should().BeEmpty();
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
