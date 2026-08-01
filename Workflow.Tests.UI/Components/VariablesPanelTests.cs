// <copyright file="VariablesPanelTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Components;

using System.Linq;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Designer.State;
using Workflow.UI.Client.Designer.State.Commands;

/// <summary>
/// 💾 Phase 3.5 (V1.3–V1.7) — the workflow variables panel: declaring, editing, renaming with
/// reference rewrites, and undo/redo.
/// </summary>
public class VariablesPanelTests : TestContext
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private static (DesignerDocument Doc, CommandStack Commands) Fixture()
        {
        var doc = new DesignerDocument { Name = "wf" };
        return (doc, new CommandStack(doc));
    }

    private IRenderedComponent<VariablesPanel> Render(DesignerDocument doc, CommandStack commands)
        => this.RenderComponent<VariablesPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.Commands, commands));

    [Fact]
    public void EmptyDocument_ShowsTheEmptyHint()
    {
        var (doc, commands) = Fixture();

        var panel = this.Render(doc, commands);

        panel.Find("[data-testid=vars-empty]").Should().NotBeNull();
    }

    [Fact]
    public void AddVariable_DeclaresItAndIsUndoable()
    {
        var (doc, commands) = Fixture();
        var panel = this.Render(doc, commands);

        panel.Find("[data-testid=var-add]").Click();
        panel.Find("[data-testid=var-name]").Input("orderId");
        panel.Find("[data-testid=var-initial]").Input("abc");
        panel.Find("[data-testid=var-save]").Click();

        doc.Variables.Should().ContainKey("orderId");
        WorkflowVariables.Parse("orderId", doc.Variables["orderId"]).InitialValue!.Value
            .GetString().Should().Be("abc");

        commands.Undo();
        doc.Variables.Should().NotContainKey("orderId", because: "every edit must participate in undo");

        commands.Redo();
        doc.Variables.Should().ContainKey("orderId");
    }

    [Fact]
    public void AddVariable_RejectsCaseInsensitiveCollision()
    {
        var (doc, commands) = Fixture();
        doc.Variables["count"] = Json("""{ "name": "count", "type": 1 }""");
        var panel = this.Render(doc, commands);

        panel.Find("[data-testid=var-add]").Click();
        panel.Find("[data-testid=var-name]").Input("Count");
        panel.Find("[data-testid=var-save]").Click();

        panel.Find("[data-testid=var-error]").TextContent.Should().Contain("case-insensitive");
        doc.Variables.Should().NotContainKey("Count");
    }

    [Fact]
    public void AddVariable_RejectsInvalidJsonInitialValue()
    {
        var (doc, commands) = Fixture();
        var panel = this.Render(doc, commands);

        panel.Find("[data-testid=var-add]").Click();
        panel.Find("[data-testid=var-name]").Input("payload");
        panel.Find("[data-testid=var-type]").Change(nameof(VariableValueType.Object));
        panel.Find("[data-testid=var-initial]").Input("{ not json");
        panel.Find("[data-testid=var-save]").Click();

        panel.Find("[data-testid=var-error]").TextContent.Should().Contain("valid JSON");
        doc.Variables.Should().BeEmpty();
    }

    [Fact]
    public void SecretVariable_CannotCarryAnInitialValue()
    {
        // A workflow definition is exported and version-controlled, so it must never hold a
        // credential — the value editor is replaced by an explanation.
        var (doc, commands) = Fixture();
        var panel = this.Render(doc, commands);

        panel.Find("[data-testid=var-add]").Click();
        panel.Find("[data-testid=var-name]").Input("apiKey");
        panel.Find("[data-testid=var-initial]").Input("super-secret");
        panel.Find("[data-testid=var-secret]").Change(true);

        panel.Find("[data-testid=var-secret-novalue]").Should().NotBeNull();
        panel.FindAll("[data-testid=var-initial]").Should().BeEmpty();

        panel.Find("[data-testid=var-save]").Click();

        var declared = WorkflowVariables.Parse("apiKey", doc.Variables["apiKey"]);
        declared.IsSecret.Should().BeTrue();
        declared.InitialValue.Should().BeNull(because: "a secret's value must never enter the definition");
        doc.Variables["apiKey"].GetRawText().Should().NotContain("super-secret");
    }

    [Fact]
    public void EditVariable_UpdatesDeclarationAndPreservesUnknownFields()
    {
        var (doc, commands) = Fixture();
        doc.Variables["count"] = Json("""{ "name": "count", "type": 1, "futureThing": 9 }""");
        var panel = this.Render(doc, commands);

        panel.Find("[data-testid=var-edit-count]").Click();
        panel.Find("[data-testid=var-description]").Input("orders processed");
        panel.Find("[data-testid=var-save]").Click();

        var raw = doc.Variables["count"];
        raw.GetProperty("description").GetString().Should().Be("orders processed");
        raw.GetProperty("futureThing").GetInt32().Should().Be(9, because: "the designer must not drop fields it doesn't model");
    }

    [Fact]
    public void RemoveVariable_IsUndoable()
    {
        var (doc, commands) = Fixture();
        doc.Variables["count"] = Json("""{ "name": "count", "type": 1 }""");
        var panel = this.Render(doc, commands);

        panel.Find("[data-testid=var-remove-count]").Click();
        doc.Variables.Should().NotContainKey("count");

        commands.Undo();
        doc.Variables.Should().ContainKey("count");
    }

    [Fact]
    public void UsageCount_ReflectsReferencingNodes()
    {
        var (doc, commands) = Fixture();
        doc.Variables["host"] = Json("""{ "name": "host", "type": 0 }""");
        doc.Nodes.Add(new DesignerNode
        {
            Id = "http1",
            ModuleId = "builtin.http",
            Name = "Fetch",
            Properties = { ["url"] = JsonValues.FromString("{{Variable.host}}/orders") },
        });

        var panel = this.Render(doc, commands);

        panel.Find("[data-testid=var-usage-host]").TextContent.Should().Contain("1");
    }

    [Fact]
    public void Rename_RewritesReferencesAsOneUndoableUnit()
    {
        var (doc, commands) = Fixture();
        doc.Variables["host"] = Json("""{ "name": "host", "type": 0 }""");
        doc.Nodes.Add(new DesignerNode
        {
            Id = "http1",
            ModuleId = "builtin.http",
            Name = "Fetch",
            Properties = { ["url"] = JsonValues.FromString("{{Variable.host}}/orders") },
        });
        var panel = this.Render(doc, commands);

        panel.Find("[data-testid=var-edit-host]").Click();
        panel.Find("[data-testid=var-name]").Input("apiHost");
        panel.Find("[data-testid=var-save]").Click();

        doc.Variables.Should().ContainKey("apiHost").And.NotContainKey("host");
        JsonValues.ToText(doc.FindNode("http1")!.Properties["url"]).Should().Be("{{Variable.apiHost}}/orders");

        // One undo must restore both the declaration and the references — a half-undone rename
        // would leave tokens pointing at a variable that no longer exists.
        commands.Undo();
        doc.Variables.Should().ContainKey("host");
        JsonValues.ToText(doc.FindNode("http1")!.Properties["url"]).Should().Be("{{Variable.host}}/orders");
    }

    [Fact]
    public void Rename_WithoutOptIn_LeavesReferencesAlone()
    {
        var (doc, commands) = Fixture();
        doc.Variables["host"] = Json("""{ "name": "host", "type": 0 }""");
        doc.Nodes.Add(new DesignerNode
        {
            Id = "http1",
            ModuleId = "builtin.http",
            Name = "Fetch",
            Properties = { ["url"] = JsonValues.FromString("{{Variable.host}}/orders") },
        });
        var panel = this.Render(doc, commands);

        panel.Find("[data-testid=var-edit-host]").Click();
        panel.Find("[data-testid=var-name]").Input("apiHost");
        panel.Find("[data-testid=var-rename-refs]").Change(false);
        panel.Find("[data-testid=var-save]").Click();

        JsonValues.ToText(doc.FindNode("http1")!.Properties["url"]).Should().Be("{{Variable.host}}/orders");
    }

    [Fact]
    public void Declarations_SurviveTheDtoRoundTrip()
    {
        var (doc, _) = Fixture();
        doc.Variables["count"] = Json("""{ "name": "count", "type": 1, "seed": 1, "isSecret": false, "unknown": "keep" }""");

        var restored = DesignerDocument.FromDto(doc.ToDto(), _ => null);

        restored.Variables.Should().ContainKey("count");
        var parsed = WorkflowVariables.Parse("count", restored.Variables["count"]);
        parsed.Seed.Should().Be(VariableSeed.AlwaysOverride);
        restored.Variables["count"].GetProperty("unknown").GetString().Should().Be("keep");
    }

    [Fact]
    public void TokenPicker_LabelsCarryTypeAndDescription()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Variables["count"] = Json("""{ "name": "count", "type": 1, "description": "orders processed so far" }""");
        doc.Nodes.Add(new DesignerNode { Id = "n1", ModuleId = "m", Name = "N" });

        var options = VariableTokens.OptionsFor(doc, "n1");

        var option = options.Single(o => o.Category == "Variables");
        option.Label.Should().Be("count — Int");
        option.Detail.Should().Be("orders processed so far");
    }
}
