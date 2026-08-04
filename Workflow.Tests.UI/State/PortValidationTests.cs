// <copyright file="PortValidationTests.cs" company="GlutenFree">
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
/// 🧪 HTTP-input plan D2/D5 — specs for <see cref="GraphValidator.ValidatePorts"/>: the client
/// mirror of the server's MA003/MA004 port rules, plus the reserved 'input' node-id warning~ 🔌.
/// </summary>
public sealed class PortValidationTests
{
    private static ModuleSchemaDto Schema(string[] inputs, string[] outputs)
        => new(
            inputs.Select(n => new PortDefinitionDto(n, n, "object", null, false, null)).ToList(),
            outputs.Select(n => new PortDefinitionDto(n, n, "object", null, false, null)).ToList(),
            new List<ModulePropertyDefinitionDto>());

    private static DesignerNode Node(string id, ModuleSchemaDto? schema, string moduleId = "m")
        => new() { Id = id, ModuleId = moduleId, Name = id, Schema = schema };

    private static DesignerConnection Conn(string s, string sp, string t, string tp)
        => new() { SourceNodeId = s, SourcePortName = sp, TargetNodeId = t, TargetPortName = tp };

    private static DesignerDocument Doc(
        IEnumerable<DesignerNode> nodes,
        IEnumerable<DesignerConnection>? conns = null)
    {
        var doc = new DesignerDocument();
        doc.Nodes.AddRange(nodes);
        doc.Connections.AddRange(conns ?? []);
        return doc;
    }

    [Fact]
    public void UndeclaredTargetPort_IsAnError_ListingValidPorts()
    {
        var doc = Doc(
            [Node("a", Schema(["input"], ["output"])), Node("b", Schema(["data"], ["output"]))],
            [Conn("a", "output", "b", "input")]);

        var issues = GraphValidator.ValidatePorts(doc);

        var issue = issues.Should().ContainSingle().Subject;
        issue.Severity.Should().Be(IssueSeverity.Error);
        issue.NodeId.Should().Be("b");
        issue.Message.Should().Contain("input port 'input'").And.Contain("accepts: data");
    }

    [Fact]
    public void TargetWithNoDeclaredInputs_SaysSo()
    {
        var doc = Doc(
            [Node("a", Schema(["input"], ["output"])), Node("start", Schema([], ["value"]), "builtin.start")],
            [Conn("a", "output", "start", "input")]);

        var issues = GraphValidator.ValidatePorts(doc);

        issues.Should().ContainSingle(i => i.Message.Contains("accepts no inputs"));
    }

    [Fact]
    public void DeclaredPorts_PassClean()
    {
        var doc = Doc(
            [Node("a", Schema(["input"], ["output"])), Node("b", Schema(["input"], ["output"]))],
            [Conn("a", "output", "b", "input")]);

        GraphValidator.ValidatePorts(doc).Should().BeEmpty();
    }

    [Fact]
    public void UndeclaredSourcePort_IsAnError()
    {
        var doc = Doc(
            [Node("a", Schema(["input"], ["success", "error"])), Node("b", Schema(["input"], ["output"]))],
            [Conn("a", "nope", "b", "input")]);

        var issues = GraphValidator.ValidatePorts(doc);

        var issue = issues.Should().ContainSingle().Subject;
        issue.NodeId.Should().Be("a");
        issue.Message.Should().Contain("output port 'nope'").And.Contain("success, error");
    }

    [Fact]
    public void DynamicOutputModule_EmptyDeclaredOutputs_IsExempt()
    {
        // Switch/partition/parallel declare no outputs — the server skips them; so do we.
        var doc = Doc(
            [Node("sw", Schema(["value"], []), "builtin.switch"), Node("b", Schema(["input"], ["output"]))],
            [Conn("sw", "case_cat", "b", "input")]);

        GraphValidator.ValidatePorts(doc).Should().BeEmpty();
    }

    [Fact]
    public void MergedModeOutputPort_IsExempt()
    {
        var node = Node("a", Schema(["input"], ["statusCode", "body"]));
        node.Properties[OutputShapingUx.PropertyName] = JsonDocument.Parse("\"merged\"").RootElement.Clone();
        var doc = Doc(
            [node, Node("b", Schema(["input"], ["output"]))],
            [Conn("a", "output", "b", "input")]);

        GraphValidator.ValidatePorts(doc).Should().BeEmpty();
    }

    [Fact]
    public void UnknownModules_NoSchema_AreSkipped()
    {
        var doc = Doc(
            [Node("a", null), Node("b", null)],
            [Conn("a", "anything", "b", "whatever")]);

        GraphValidator.ValidatePorts(doc).Should().BeEmpty();
    }

    [Fact]
    public void NodeIdNamedInput_GetsReservedWordWarning()
    {
        var doc = Doc([Node("input", Schema(["input"], ["output"]))]);

        var issues = GraphValidator.ValidatePorts(doc);

        var issue = issues.Should().ContainSingle().Subject;
        issue.Severity.Should().Be(IssueSeverity.Warning);
        issue.Message.Should().Contain("reserved");
    }

    [Fact]
    public void CaseDifferences_AreTolerated_LikeTheServer()
    {
        var doc = Doc(
            [Node("a", Schema(["input"], ["Output"])), Node("b", Schema(["Input"], ["output"]))],
            [Conn("a", "output", "b", "input")]);

        GraphValidator.ValidatePorts(doc).Should().BeEmpty();
    }
}
