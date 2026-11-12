// <copyright file="RunAfterEdgesTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.State;
using Workflow.UI.Client.Designer.State.Commands;
using Xunit;

/// <summary>
/// Plan 09 — designer rules for run-after edges: legacy <c>→ input</c> edges load as run-after,
/// run-after edges never stream and aren't port-checked, and the inspector's switch is undoable.
/// </summary>
public sealed class RunAfterEdgesTests
{
    private static ModuleSchemaDto Schema(string[] inputs, string[] outputs, bool streaming = false)
        => new(
            inputs.Select(p => new PortDefinitionDto(p, p, "object", null, false, null, streaming)).ToList(),
            outputs.Select(p => new PortDefinitionDto(p, p, "object", null, false, null, streaming)).ToList(),
            new List<ModulePropertyDefinitionDto>());

    private static DesignerDocument Doc(ModuleSchemaDto? targetSchema, params DesignerConnection[] connections)
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "a", ModuleId = "m.a", Name = "A", X = 100, Y = 100, Schema = Schema(new[] { "input" }, new[] { "output" }) });
        doc.Nodes.Add(new DesignerNode { Id = "b", ModuleId = "m.b", Name = "B", X = 400, Y = 100, Schema = targetSchema });
        doc.Connections.AddRange(connections);
        return doc;
    }

    private static DesignerConnection Data(string port) => new() { SourceNodeId = "a", SourcePortName = "output", TargetNodeId = "b", TargetPortName = port };

    [Fact]
    public void LegacyInputEdge_IntoAModuleWithoutThatPort_LoadsAsRunAfter()
    {
        var doc = Doc(Schema(new[] { "condition" }, new[] { "true", "false" }), Data("input"));

        RunAfterEdges.NormaliseLegacy(doc).Should().Be(1);

        doc.Connections.Single().IsRunAfter.Should().BeTrue();
        doc.Connections.Single().TargetPortName.Should().BeEmpty();
    }

    [Fact]
    public void InputEdges_IntoADeclaredPort_OrAnUnknownModule_StayData()
    {
        var declared = Doc(Schema(new[] { "input" }, new[] { "output" }), Data("input"));
        var unknown = Doc(null, Data("input"));

        RunAfterEdges.NormaliseLegacy(declared).Should().Be(0);
        RunAfterEdges.NormaliseLegacy(unknown).Should().Be(0, "without a schema we can't tell");
        declared.Connections.Single().IsRunAfter.Should().BeFalse();
    }

    [Fact]
    public void Loading_AWorkflow_NormalisesLegacyEdges()
    {
        var dto = new WorkflowDto(
            Guid.NewGuid(), "wf", null, "1.0.0",
            new List<NodeDto>
            {
                new("a", "m.a", "A", new Dictionary<string, System.Text.Json.JsonElement>(), null),
                new("b", "m.b", "B", new Dictionary<string, System.Text.Json.JsonElement>(), null),
            },
            new List<ConnectionDto> { new("a", "output", "b", "input") },
            null, null, null, null, null, null);
        var schemas = new Dictionary<string, ModuleSchemaDto>
        {
            ["m.a"] = Schema(new[] { "input" }, new[] { "output" }),
            ["m.b"] = Schema(new[] { "value" }, Array.Empty<string>()),
        };

        var doc = DesignerDocument.FromDto(dto, id => schemas.GetValueOrDefault(id));

        doc.Connections.Single().IsRunAfter.Should().BeTrue();
    }

    [Fact]
    public void RunAfterKey_DiffersFromADataEdgeKey()
    {
        RunAfterEdges.Create("a", "output", "b").Key.Should().NotBe(Data(string.Empty).Key);
    }

    [Fact]
    public void CanAdd_RejectsSelfDuplicateAndCycle()
    {
        var doc = Doc(Schema(new[] { "input" }, new[] { "output" }), RunAfterEdges.Create("a", "output", "b"));

        RunAfterEdges.CanAdd(doc, "a", "output", "a").Should().BeFalse("self");
        RunAfterEdges.CanAdd(doc, "a", "output", "b").Should().BeFalse("duplicate");
        RunAfterEdges.CanAdd(doc, "b", "output", "a").Should().BeFalse("cycle");
        RunAfterEdges.CanAdd(Doc(Schema(new[] { "input" }, new[] { "output" })), "a", "output", "b").Should().BeTrue();
    }

    [Fact]
    public void HitTestNodeBody_FindsTheNodeUnderThePoint()
    {
        var doc = Doc(Schema(new[] { "input" }, new[] { "output" }));

        RunAfterEdges.HitTestNodeBody(doc, new Point(420, 120))!.Id.Should().Be("b");
        RunAfterEdges.HitTestNodeBody(doc, new Point(300, 600)).Should().BeNull();
    }

    [Fact]
    public void RunAfterEdge_BetweenStreamingPorts_IsNotAStreamingEdge()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "a", ModuleId = "s", Schema = Schema(new[] { "items" }, new[] { "items" }, streaming: true) });
        doc.Nodes.Add(new DesignerNode { Id = "b", ModuleId = "s", Schema = Schema(new[] { "items" }, new[] { "items" }, streaming: true) });
        var edge = new DesignerConnection { SourceNodeId = "a", SourcePortName = "items", TargetNodeId = "b", TargetPortName = "items", Kind = DesignerConnection.RunAfterKind };

        StreamGraph.IsStreamingEdge(doc, edge).Should().BeFalse("mirrors the engine: a run-after edge carries no data");
    }

    [Fact]
    public void Validator_DoesNotPortCheck_RunAfterOrLegacyInputEdges_ButStillFlagsATypo()
    {
        var known = new HashSet<string> { "m.a", "m.b" };
        var condition = Schema(new[] { "condition" }, new[] { "true" });

        GraphValidator.Validate(Doc(condition, RunAfterEdges.Create("a", "output", "b")), known)
            .Should().NotContain(i => i.Message.Contains("doesn't declare", StringComparison.Ordinal));
        GraphValidator.Validate(Doc(condition, Data("input")), known)
            .Should().NotContain(i => i.Message.Contains("doesn't declare", StringComparison.Ordinal));
        GraphValidator.Validate(Doc(condition, Data("bogus")), known)
            .Should().Contain(i => i.Message.Contains("doesn't declare", StringComparison.Ordinal));
    }

    [Fact]
    public void ARunAfterEdge_WithAStalePortName_IsNeitherPortNorShapeChecked()
    {
        // The port is ignored on a run-after edge, even if an old name is left on it.
        var known = new HashSet<string> { "m.a", "m.b" };
        var stale = RunAfterEdges.Create("a", "output", "b");
        stale.TargetPortName = "bogus";

        GraphValidator.Validate(Doc(Schema(new[] { "condition" }, new[] { "true" }), stale), known)
            .Should().NotContain(i => i.Message.Contains("doesn't declare", StringComparison.Ordinal));

        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "a", ModuleId = "s", Schema = Schema(new[] { "items" }, new[] { "items" }, streaming: true) });
        doc.Nodes.Add(new DesignerNode { Id = "b", ModuleId = "m", Schema = Schema(new[] { "in" }, new[] { "out" }) });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "a", SourcePortName = "items", TargetNodeId = "b", TargetPortName = "in", Kind = DesignerConnection.RunAfterKind });

        GraphValidator.ValidateStreaming(doc).Should().BeEmpty("a run-after edge carries nothing, so there's no shape to mismatch");
    }

    [Fact]
    public void SetConnectionTarget_SwitchesKind_AndUndoes()
    {
        var doc = Doc(Schema(new[] { "value" }, new[] { "output" }), Data("value"));
        var stack = new CommandStack(doc);
        var command = new SetConnectionTargetCommand(doc.Connections[0], runAfter: true, port: string.Empty);

        stack.Execute(command);

        doc.Connections[0].IsRunAfter.Should().BeTrue();
        doc.Connections[0].TargetPortName.Should().BeEmpty();
        doc.Connections[0].Key.Should().Be(command.AfterKey);

        stack.Undo();

        doc.Connections[0].IsRunAfter.Should().BeFalse();
        doc.Connections[0].TargetPortName.Should().Be("value");
    }
}
