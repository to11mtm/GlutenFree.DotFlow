// <copyright file="NonUpstreamReferencePanelTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Components;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// §10.1 — the properties panel explains a <c>{{node.port}}</c> to a node that isn't upstream and
/// offers "wire it in" and "pass it via a variable"; nothing changes unless one is clicked.
/// </summary>
public sealed class NonUpstreamReferencePanelTests : TestContext
{
    public NonUpstreamReferencePanelTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new Workflow.UI.Client.Scripts.State.ScriptStudioHandoff());
        this.Services.AddSingleton(new Workflow.UI.Client.Linq.State.LinqStudioHandoff());
    }

    private static PortDefinitionDto Port(string name) => new(name, name, "Object", null, false, null);

    private (IRenderedComponent<PropertiesPanel> Cut, DesignerDocument Doc) Render(params string[] sinkInputs)
    {
        var doc = new DesignerDocument();
        doc.Nodes.Add(new DesignerNode { Id = "a", ModuleId = "m", Name = "A" });
        doc.Nodes.Add(new DesignerNode { Id = "b", ModuleId = "m", Name = "B" });
        doc.Nodes.Add(new DesignerNode
        {
            Id = "sink",
            ModuleId = "m.sink",
            Name = "Sink",
            Schema = new ModuleSchemaDto(
                sinkInputs.Select(Port).ToList(),
                new List<PortDefinitionDto>(),
                new List<ModulePropertyDefinitionDto> { new("url", "url", "String", null, false, null, "Text", null, true) }),
            Properties = { ["url"] = JsonSerializer.SerializeToElement("/x/{{b.output}}") },
        });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "a", SourcePortName = "output", TargetNodeId = "b", TargetPortName = "input" });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "a", SourcePortName = "output", TargetNodeId = "sink", TargetPortName = "input" });

        var sel = new SelectionState();
        sel.SelectNode("sink");
        var setVariableSchema = new ModuleSchemaDto(new List<PortDefinitionDto> { Port("value") }, new(), new());
        var cut = this.RenderComponent<PropertiesPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, sel)
            .Add(x => x.Commands, new CommandStack(doc))
            .Add(x => x.SchemaFor, id => Task.FromResult<ModuleSchemaDto?>(id == UpstreamReferences.SetVariableModuleId ? setVariableSchema : null)));
        return (cut, doc);
    }

    [Fact]
    public void NonUpstreamReference_IsExplained_AndNothingChangesByItself()
    {
        var (cut, doc) = this.Render("input", "extra");

        cut.Find("[data-testid=nonupstream-url-b]").TextContent.Should().Contain("reads 'B', which isn't upstream of this node");
        cut.FindAll("[data-testid=nonupstream-wire-url-b]").Should().ContainSingle();
        cut.FindAll("[data-testid=nonupstream-var-url-b]").Should().ContainSingle();
        doc.Connections.Should().HaveCount(2);
        doc.Nodes.Should().HaveCount(3);
    }

    [Fact]
    public void WireItIn_IsHiddenWhenNoPortIsFree()
    {
        var (cut, _) = this.Render("input");

        cut.FindAll("[data-testid=nonupstream-wire-url-b]").Should().BeEmpty();
        cut.FindAll("[data-testid=nonupstream-var-url-b]").Should().ContainSingle();
    }

    [Fact]
    public void WireItIn_ConnectsTheSource_AndTheErrorGoes()
    {
        var (cut, doc) = this.Render("input", "extra");

        cut.Find("[data-testid=nonupstream-wire-url-b]").Click();

        doc.Connections.Should().Contain(c => c.SourceNodeId == "b" && c.TargetNodeId == "sink" && c.TargetPortName == "extra");
        cut.FindAll("[data-testid=nonupstream-url-b]").Should().BeEmpty();
    }

    [Fact]
    public void ViaVariable_AddsASetVariableWithItsSchema_AndRewritesTheField()
    {
        var (cut, doc) = this.Render("input");

        cut.Find("[data-testid=nonupstream-var-url-b]").Click();

        var setter = doc.Nodes.Single(n => n.ModuleId == UpstreamReferences.SetVariableModuleId);
        setter.Schema.Should().NotBeNull(because: "the quick-fix asks the designer for the module schema");
        cut.Find("[data-testid=editor-url]").GetAttribute("value").Should().Be("/x/{{Variable.b_output}}");
        cut.FindAll("[data-testid=nonupstream-url-b]").Should().BeEmpty();
    }
}
