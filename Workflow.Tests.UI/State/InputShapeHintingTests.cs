// <copyright file="InputShapeHintingTests.cs" company="GlutenFree">
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
/// 🧪 Input-shape hinting T1/T2/T7/T8 — specs for <see cref="InputShape"/>,
/// <see cref="FanInShape"/>, the expanded <see cref="VariableTokens.OptionsFor"/>, and the
/// branch-reorder command~ 🔎.
/// </summary>
public sealed class InputShapeHintingTests
{
    private static ModuleSchemaDto Schema(string[] inputs, (string Name, string? Type, string? Desc)[] outputs)
        => new(
            inputs.Select(n => new PortDefinitionDto(n, n, "object", null, false, null)).ToList(),
            outputs.Select(o => new PortDefinitionDto(o.Name, o.Name, o.Type, o.Desc, false, null)).ToList(),
            new List<ModulePropertyDefinitionDto>());

    private static DesignerNode Node(string id, ModuleSchemaDto? schema = null, string moduleId = "m")
        => new() { Id = id, ModuleId = moduleId, Name = id, Schema = schema };

    private static DesignerConnection Conn(string s, string sp, string t, string tp = "input")
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

    private static DesignerNode MergedHttp(string id = "http-1")
    {
        var node = Node(id, Schema(
            ["input"],
            [("statusCode", "int", "HTTP status"), ("body", "string", "Response body")]));
        node.Properties[OutputShapingUx.PropertyName] = JsonDocument.Parse("\"merged\"").RootElement.Clone();
        return node;
    }

    // ─── T1 — InputShape ────────────────────────────────────────────────────────

    [Fact]
    public void MergedUpstream_ExpandsIntoPreCollapsePorts()
    {
        var doc = Doc([MergedHttp(), Node("b", Schema(["input"], []))], [Conn("http-1", "output", "b")]);

        var keys = InputShape.AddressableKeys(doc, doc.FindNode("http-1")!);

        keys.Select(k => k.Token).Should().Contain(
            ["{{http-1.output}}", "{{http-1.output.statusCode}}", "{{http-1.output.body}}"]);
        keys.Single(k => k.Token == "{{http-1.output.statusCode}}").DataType.Should().Be("int");
    }

    [Fact]
    public void PlainUpstream_HasNoSubKeys()
    {
        var doc = Doc([Node("a", Schema([], [("output", "object", null)]))]);

        var keys = InputShape.AddressableKeys(doc, doc.FindNode("a")!);

        keys.Should().ContainSingle(k => k.Token == "{{a.output}}");
    }

    [Fact]
    public void IncomingFor_ListsWiredPorts_WithSelfInputPrefixedKeys()
    {
        var doc = Doc([MergedHttp(), Node("b", Schema(["input"], []))], [Conn("http-1", "output", "b")]);

        var incoming = InputShape.IncomingFor(doc, "b");

        var row = incoming.Should().ContainSingle().Subject;
        row.PortName.Should().Be("input");
        row.SourceNodeId.Should().Be("http-1");
        row.Keys.Select(k => k.Token).Should().Contain("{{input.statusCode}}");
    }

    [Fact]
    public void SelfInputWired_TracksTheInputPortOnly()
    {
        var doc = Doc(
            [Node("a"), Node("b"), Node("c")],
            [Conn("a", "output", "b"), Conn("a", "output", "c", "data")]);

        InputShape.SelfInputWired(doc, "b").Should().BeTrue();
        InputShape.SelfInputWired(doc, "c").Should().BeFalse();
    }

    // ─── T7 — FanInShape ────────────────────────────────────────────────────────

    private static DesignerDocument FanInDoc(string mode = "named")
    {
        var fanin = Node(
            "fan",
            Schema(["branches"], [("result", "object", "Aggregated payload"), ("count", "int", null), ("done", "object", null)]),
            "builtin.fanin");
        fanin.Properties["mode"] = JsonDocument.Parse($"\"{mode}\"").RootElement.Clone();
        return Doc(
            [Node("h1", Schema([], [("body", "string", null)])), Node("h2", Schema([], [("body", "string", null)])), Node("s1", Schema([], [("result", "object", null)])), fanin],
            [Conn("h1", "body", "fan", "branches"), Conn("h2", "body", "fan", "branches"), Conn("s1", "result", "fan", "branches")]);
    }

    [Fact]
    public void Branches_AreInConnectionDeclarationOrder()
    {
        var branches = FanInShape.Branches(FanInDoc(), "fan");

        branches.Select(b => b.SourceNodeId).Should().Equal("h1", "h2", "s1");
        branches.Select(b => b.Index).Should().Equal(0, 1, 2);
    }

    [Fact]
    public void NamedKeys_MirrorTheEngineCollisionRule()
    {
        // h1.body and h2.body collide → nodeId.port; s1.result is unique → bare port name.
        var keys = FanInShape.NamedKeys(FanInShape.Branches(FanInDoc(), "fan"));

        keys.Should().Equal("h1.body", "h2.body", "result");
    }

    [Fact]
    public void MergeKeys_UnionOfUpstreamPorts_LastWriterWins()
    {
        var merged = FanInShape.MergeKeys(FanInDoc("merge"), FanInShape.Branches(FanInDoc("merge"), "fan"));

        // 'body' appears in branches 0 and 1 → branch 1 wins; 'result' only in branch 2.
        merged.Should().Contain(("body", 1)).And.Contain(("result", 2));
    }

    [Fact]
    public void ShapeSummary_FollowsTheMode()
    {
        var doc = FanInDoc("concat");
        FanInShape.ShapeSummary(doc, doc.FindNode("fan")!).Should().Contain("list of 3");

        var named = FanInDoc("named");
        FanInShape.ShapeSummary(named, named.FindNode("fan")!).Should().Contain("h1.body");

        var last = FanInDoc("last");
        FanInShape.ShapeSummary(last, last.FindNode("fan")!).Should().Contain("branch 3");
    }

    [Fact]
    public void ShapeSummary_NoBranches_ExplainsTheMechanism()
    {
        var doc = Doc([Node("fan", null, "builtin.fanin")]);

        FanInShape.ShapeSummary(doc, doc.FindNode("fan")!).Should().Contain("No branches yet");
    }

    // ─── T2 — token picker options ──────────────────────────────────────────────

    [Fact]
    public void WiredInputPort_OffersSelfInputTokenFirst()
    {
        var doc = Doc([MergedHttp(), Node("b", Schema(["input"], []))], [Conn("http-1", "output", "b")]);

        var options = VariableTokens.OptionsFor(doc, "b");

        options[0].Token.Should().Be("{{input}}");
        options[0].Category.Should().Be(VariableTokens.SelfInputCategory);
        options.Should().Contain(o => o.Token == "{{input.body}}");
    }

    [Fact]
    public void UnwiredInput_OffersNoSelfInputToken()
    {
        var doc = Doc([Node("a"), Node("b")]);

        VariableTokens.OptionsFor(doc, "b").Should().NotContain(o => o.Token == "{{input}}");
    }

    [Fact]
    public void MergedUpstream_TokensIncludeSubKeys_WithTypeDetail()
    {
        var doc = Doc([MergedHttp(), Node("b", Schema(["input"], []))], [Conn("http-1", "output", "b")]);

        var options = VariableTokens.OptionsFor(doc, "b");

        var sub = options.Should().Contain(o => o.Token == "{{http-1.output.statusCode}}").Subject;
        sub.Detail.Should().Contain("int").And.Contain("HTTP status");
    }

    [Fact]
    public void NamedFanInUpstream_TokensIncludeComputedKeys()
    {
        var doc = FanInDoc();
        doc.Nodes.Add(Node("after", Schema(["input"], [])));
        doc.Connections.Add(Conn("fan", "result", "after"));

        var options = VariableTokens.OptionsFor(doc, "after");

        options.Should().Contain(o => o.Token == "{{fan.result.h1.body}}")
            .And.Contain(o => o.Token == "{{fan.result.result}}");
    }

    // ─── T8 — reorder command ───────────────────────────────────────────────────

    [Fact]
    public void ReorderBranch_SwapsOnlyTheNodesBranches_AndUndoes()
    {
        var doc = FanInDoc();
        doc.Nodes.Add(Node("x"));
        doc.Nodes.Add(Node("y"));
        doc.Connections.Insert(1, Conn("x", "output", "y")); // unrelated edge interleaved

        var cmd = new Workflow.UI.Client.Designer.State.Commands.ReorderIncomingConnectionCommand("fan", 0, 1);
        cmd.Do(doc);

        FanInShape.Branches(doc, "fan").Select(b => b.SourceNodeId).Should().Equal("h2", "h1", "s1");
        doc.Connections.Should().Contain(c => c.SourceNodeId == "x"); // untouched

        cmd.Undo(doc);
        FanInShape.Branches(doc, "fan").Select(b => b.SourceNodeId).Should().Equal("h1", "h2", "s1");
    }

    [Fact]
    public void ReorderBranch_OutOfRange_IsIgnored()
    {
        var doc = FanInDoc();

        new Workflow.UI.Client.Designer.State.Commands.ReorderIncomingConnectionCommand("fan", 0, 9).Do(doc);

        FanInShape.Branches(doc, "fan").Select(b => b.SourceNodeId).Should().Equal("h1", "h2", "s1");
    }
}
