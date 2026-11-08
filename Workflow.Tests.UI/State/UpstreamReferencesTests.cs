// <copyright file="UpstreamReferencesTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// §10.1 — <c>{{nodeId.port}}</c> resolves for any ancestor (across bodies); the designer errors on a
/// reference to a node that isn't upstream and offers "wire it in" / "pass it via a variable".
/// </summary>
public class UpstreamReferencesTests
{
    private static JsonElement Str(string value) => JsonSerializer.SerializeToElement(value);

    private static PortDefinitionDto Port(string name) => new(name, name, "Object", null, false, null);

    /// <summary>a → b; a → sink (so b is a sibling, not upstream of sink).</summary>
    private static DesignerDocument Doc(string value, params string[] sinkInputs)
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "a", ModuleId = "m", Name = "A", X = 0, Y = 0 });
        doc.Nodes.Add(new DesignerNode { Id = "b", ModuleId = "m", Name = "B", X = 300, Y = 0 });
        doc.Nodes.Add(new DesignerNode
        {
            Id = "sink",
            ModuleId = "m.sink",
            Name = "Sink",
            Schema = new ModuleSchemaDto(
                (sinkInputs.Length == 0 ? new[] { "input" } : sinkInputs).Select(Port).ToList(),
                new List<PortDefinitionDto>(),
                new List<ModulePropertyDefinitionDto> { new("p", "p", "String", null, false, null, "Text", null, true) }),
            Properties = { ["p"] = Str(value) },
        });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "a", SourcePortName = "output", TargetNodeId = "b", TargetPortName = "input" });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "a", SourcePortName = "output", TargetNodeId = "sink", TargetPortName = "input" });
        return doc;
    }

    [Fact]
    public void Ancestor_IsClean_NonAncestor_IsAnError()
    {
        VariableLint.Validate(Doc("{{a.output}}")).Should().BeEmpty();

        var issues = VariableLint.Validate(Doc("{{b.output}}"));
        issues.Should().ContainSingle().Which.Severity.Should().Be(IssueSeverity.Error);
        issues[0].Message.Should().Contain("reads 'B', which isn't upstream of this node");
    }

    [Fact]
    public void AnAncestorAcrossABodyBoundary_IsClean()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "auth", ModuleId = "m", Name = "Auth" });
        doc.Nodes.Add(new DesignerNode { Id = "loop", ModuleId = "builtin.loop.foreach", Name = "Loop" });
        doc.Nodes.Add(new DesignerNode
        {
            Id = "call",
            ModuleId = "m.sink",
            Name = "Call",
            Schema = new ModuleSchemaDto(new List<PortDefinitionDto> { Port("input") }, new(), new List<ModulePropertyDefinitionDto> { new("p", "p", "String", null, false, null, "Text", null, true) }),
            Properties = { ["p"] = Str("{{auth.output}}") },
        });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "auth", SourcePortName = "output", TargetNodeId = "loop", TargetPortName = "input" });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "loop", SourcePortName = "loopBody", TargetNodeId = "call", TargetPortName = "input" });

        VariableLint.Validate(doc).Should().BeEmpty();
    }

    [Fact]
    public void Find_ReportsOnlyNonUpstream_AndPicksAFreePortToWireInto()
    {
        var doc = Doc("{{a.output}} {{b.output}} {{b.output.x}} {{Variable.v}} {{ghost.output}}", "input", "extra");

        var found = UpstreamReferences.Find(doc, doc.FindNode("sink")!, "p", doc.FindNode("sink")!.Properties["p"].GetString());

        found.Should().ContainSingle();
        found[0].SourceNodeId.Should().Be("b");
        found[0].WirePort.Should().Be("extra", because: "'input' is already wired from a");
    }

    [Fact]
    public void WireItIn_IsOfferedOnlyWhenAPortIsFree_AndMakesTheSourceUpstream()
    {
        var full = Doc("{{b.output}}");
        UpstreamReferences.Find(full, full.FindNode("sink")!, "p", "{{b.output}}").Single().WirePort.Should().BeNull();

        var doc = Doc("{{b.output}}", "input", "extra");
        var reference = UpstreamReferences.Find(doc, doc.FindNode("sink")!, "p", "{{b.output}}").Single();
        var stack = new CommandStack(doc);

        stack.Execute(UpstreamReferences.WireItIn(doc.FindNode("sink")!, reference)!);

        doc.Connections.Should().Contain(c => c.SourceNodeId == "b" && c.TargetNodeId == "sink" && c.TargetPortName == "extra");
        VariableLint.Validate(doc).Should().BeEmpty();
        stack.Undo();
        VariableLint.Validate(doc).Should().ContainSingle();
    }

    [Fact]
    public void ViaVariable_AddsASetVariable_RewritesTheToken_AsOneUndoableStep()
    {
        var doc = Doc("x={{b.output.id}} y={{b.output}} z={{b.outputs}}");
        var sink = doc.FindNode("sink")!;
        var value = sink.Properties["p"].GetString()!;
        var reference = UpstreamReferences.Find(doc, sink, "p", value).Single(r => r.SourcePort == "output");
        var stack = new CommandStack(doc);

        var (command, name, rewritten) = UpstreamReferences.ViaVariable(doc, sink, reference, value, setVariableSchema: null);
        stack.Execute(command);

        name.Should().Be("b_output");
        rewritten.Should().Be("x={{Variable.b_output.id}} y={{Variable.b_output}} z={{b.outputs}}",
            because: "only b.output (with its paths) is rewritten");
        sink.Properties["p"].GetString().Should().Be(rewritten);
        var setter = doc.Nodes.Single(n => n.ModuleId == UpstreamReferences.SetVariableModuleId);
        setter.Properties["name"].GetString().Should().Be("b_output");
        doc.Connections.Should().Contain(c => c.SourceNodeId == "b" && c.SourcePortName == "output" && c.TargetNodeId == setter.Id && c.TargetPortName == "value");

        stack.Undo();
        doc.Nodes.Should().NotContain(n => n.ModuleId == UpstreamReferences.SetVariableModuleId);
        sink.Properties["p"].GetString().Should().Be(value);
    }

    [Fact]
    public void ViaVariable_PicksAnUnusedName()
    {
        var doc = Doc("{{b.output}}");
        doc.Variables["b_output"] = JsonDocument.Parse("""{ "name": "b_output", "type": 0, "initialValue": "x" }""").RootElement.Clone();
        var sink = doc.FindNode("sink")!;
        var reference = UpstreamReferences.Find(doc, sink, "p", "{{b.output}}").Single();

        UpstreamReferences.ViaVariable(doc, sink, reference, "{{b.output}}", null).VariableName.Should().Be("b_output2");
    }
}
