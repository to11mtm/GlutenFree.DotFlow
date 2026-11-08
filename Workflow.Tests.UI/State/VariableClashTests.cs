// <copyright file="VariableClashTests.cs" company="GlutenFree">
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
/// §10.2 — port values stopped doubling as variables, so a <c>{{Variable.x}}</c> in a node whose
/// input port <c>x</c> is wired reads the variable, not the port. The designer flags that and offers
/// a fix; construct bodies get <c>item</c> / <c>index</c> / <c>error</c> as body variables.
/// </summary>
public class VariableClashTests
{
    private static JsonElement Str(string value) => JsonSerializer.SerializeToElement(value);

    private static JsonElement Decl(string name)
        => JsonDocument.Parse($$"""{ "name": "{{name}}", "type": 1, "initialValue": 99 }""").RootElement.Clone();

    private static ModuleSchemaDto Templated(params string[] properties)
        => new(
            new List<PortDefinitionDto>(),
            new List<PortDefinitionDto>(),
            properties.Select(p => new ModulePropertyDefinitionDto(p, p, "String", null, false, null, "Text", null, true)).ToList());

    /// <summary>src.value → sink.count; sink.p = <paramref name="value"/>.</summary>
    private static DesignerDocument Wired(string value, bool declareCount)
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "src", ModuleId = "m.src", Name = "Source" });
        doc.Nodes.Add(new DesignerNode { Id = "sink", ModuleId = "m.sink", Name = "Sink", Schema = Templated("p"), Properties = { ["p"] = Str(value) } });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "src", SourcePortName = "value", TargetNodeId = "sink", TargetPortName = "count" });
        if (declareCount)
        {
            doc.Variables["count"] = Decl("count");
        }

        return doc;
    }

    [Fact]
    public void Clash_WithADeclaredVariable_IsAWarning()
    {
        var doc = Wired("{{Variable.count}}", declareCount: true);

        var issues = VariableLint.Validate(doc);

        issues.Should().ContainSingle().Which.Severity.Should().Be(IssueSeverity.Warning);
        issues[0].Message.Should().Contain("reads the variable 'count'").And.Contain("{{src.value}}");
    }

    [Fact]
    public void Clash_WithNoVariable_IsOneErrorThatNamesTheIncomingValue()
    {
        var doc = Wired("{{Variable.count}}", declareCount: false);

        var issues = VariableLint.Validate(doc);

        issues.Should().ContainSingle(because: "the clash replaces the plain 'undeclared' error rather than doubling it")
            .Which.Severity.Should().Be(IssueSeverity.Error);
        issues[0].Message.Should().Contain("there's no variable 'count'").And.Contain("{{src.value}}");
    }

    [Fact]
    public void NoWiredPortOfThatName_IsNotAClash()
    {
        var doc = Wired("{{Variable.total}}", declareCount: true);
        doc.Variables["total"] = Decl("total");

        VariableLint.Clashes(doc, doc.FindNode("sink")!, "p", "{{Variable.total}}").Should().BeEmpty();
    }

    [Fact]
    public void ClashInsideAnExpression_IsFound()
    {
        var doc = Wired("{{Variable.count > 5}}", declareCount: true);

        VariableLint.Clashes(doc, doc.FindNode("sink")!, "p", "{{Variable.count > 5}}")
            .Should().ContainSingle().Which.Name.Should().Be("count");
    }

    [Fact]
    public void UseIncomingValue_RewritesTheTokenKeepingPathsAndExpressions()
    {
        var doc = Wired("x", declareCount: true);
        var clash = VariableLint.Clashes(doc, doc.FindNode("sink")!, "p", "{{Variable.count}}").Single();

        VariableLint.UseIncomingValue("n={{Variable.count}} a={{Variable.count.a}} {{Variable.count > 5}} {{Variable.counter}} Variable.count", clash)
            .Should().Be("n={{src.value}} a={{src.value.a}} {{src.value > 5}} {{Variable.counter}} Variable.count",
                because: "only the clashing name changes, and only inside tokens");
    }

    [Fact]
    public void KeepTheVariable_SilencesTheClash_UntilTheWiringChanges()
    {
        var doc = Wired("{{Variable.count}}", declareCount: true);
        var sink = doc.FindNode("sink")!;
        var clash = VariableLint.Clashes(doc, sink, "p", "{{Variable.count}}").Single();

        sink.Metadata[VariableLint.ClashAckKey] = VariableLint.AcknowledgedWith(sink, clash);

        sink.Metadata[VariableLint.ClashAckKey].Should().Be("count@src.value");
        VariableLint.Validate(doc).Should().BeEmpty();

        // Rewired from another source → a different situation, so ask again.
        doc.Nodes.Add(new DesignerNode { Id = "other", ModuleId = "m.src", Name = "Other" });
        doc.Connections.Clear();
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "other", SourcePortName = "value", TargetNodeId = "sink", TargetPortName = "count" });
        VariableLint.Validate(doc).Should().ContainSingle().Which.Severity.Should().Be(IssueSeverity.Warning);
    }

    [Fact]
    public void AcknowledgedWith_AppendsWithoutDuplicates()
    {
        var doc = Wired("{{Variable.count}}", declareCount: true);
        var sink = doc.FindNode("sink")!;
        sink.Metadata[VariableLint.ClashAckKey] = "other@x.y";
        var clash = VariableLint.Clashes(doc, sink, "p", "{{Variable.count}}").Single();

        var once = VariableLint.AcknowledgedWith(sink, clash);
        sink.Metadata[VariableLint.ClashAckKey] = once;

        once.Should().Be("other@x.y,count@src.value");
        VariableLint.AcknowledgedWith(sink, clash).Should().Be(once);
    }

    [Fact]
    public void RenameWithReferences_RenamesTheDeclarationAndEveryToken_AsOneUndoableStep()
    {
        var doc = Wired("{{Variable.count}} and {{Variable.count.x}}", declareCount: true);
        var stack = new CommandStack(doc);

        stack.Execute(WorkflowVariables.RenameWithReferences(doc, "count", "threshold"));

        doc.Variables.Keys.Should().Equal("threshold");
        doc.FindNode("sink")!.Properties["p"].GetString().Should().Be("{{Variable.threshold}} and {{Variable.threshold.x}}");
        VariableLint.Validate(doc).Should().BeEmpty(because: "the names no longer collide");

        stack.Undo();
        doc.Variables.Keys.Should().Equal("count");
        doc.FindNode("sink")!.Properties["p"].GetString().Should().Be("{{Variable.count}} and {{Variable.count.x}}");
    }

    // ── Body values (item / index / error) ────────────────────────────────────────────────────

    private static DesignerDocument Body(string ownerModule, string ownerPort, string value)
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "owner", ModuleId = ownerModule, Name = "Owner" });
        doc.Nodes.Add(new DesignerNode { Id = "first", ModuleId = "m.pass", Name = "First" });
        doc.Nodes.Add(new DesignerNode { Id = "deep", ModuleId = "m.sink", Name = "Deep", Schema = Templated("p"), Properties = { ["p"] = Str(value) } });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "owner", SourcePortName = ownerPort, TargetNodeId = "first", TargetPortName = "input" });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "first", SourcePortName = "output", TargetNodeId = "deep", TargetPortName = "input" });
        return doc;
    }

    [Theory]
    [InlineData("builtin.loop.foreach", "loopBody", "{{Variable.item}}-{{Variable.index}}")]
    [InlineData("builtin.fanout", "branch", "{{Variable.item}}-{{Variable.index}}")]
    [InlineData("builtin.trycatch", "catch", "{{Variable.error.message}}")]
    public void BodyValues_ResolveInsideTheirBody(string ownerModule, string ownerPort, string value)
    {
        VariableLint.Validate(Body(ownerModule, ownerPort, value)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("builtin.loop.foreach", "done", "{{Variable.item}}")]
    [InlineData("builtin.trycatch", "try", "{{Variable.error}}")]
    [InlineData("builtin.loop.while", "loopBody", "{{Variable.item}}")]
    public void BodyValues_DontExistOutsideTheirBody(string ownerModule, string ownerPort, string value)
    {
        VariableLint.Validate(Body(ownerModule, ownerPort, value))
            .Should().ContainSingle().Which.Severity.Should().Be(IssueSeverity.Error);
    }

    [Fact]
    public void Picker_OffersBodyValuesInsideALoopBody()
    {
        var doc = Body("builtin.loop.foreach", "loopBody", "x");

        var options = VariableTokens.OptionsFor(doc, "deep");

        options.Where(o => o.Category == VariableTokens.BodyValuesCategory).Select(o => o.Token)
            .Should().Equal("{{Variable.item}}", "{{Variable.index}}");
        VariableTokens.OptionsFor(doc, "owner").Should().NotContain(o => o.Category == VariableTokens.BodyValuesCategory);
    }
}
