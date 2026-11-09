// <copyright file="NodeErrorHandlingTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>§12.7 — the designer's "On error" settings: read/write, handler candidates, lint, links.</summary>
public class NodeErrorHandlingTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>a → b → c, plus a standalone handler h.</summary>
    private static DesignerDocument Doc()
    {
        var doc = new DesignerDocument { Name = "wf" };
        foreach (var id in new[] { "a", "b", "c", "h" })
        {
            doc.Nodes.Add(new DesignerNode { Id = id, ModuleId = "m", Name = id.ToUpperInvariant() });
        }

        doc.Connections.Add(new DesignerConnection { SourceNodeId = "a", SourcePortName = "output", TargetNodeId = "b", TargetPortName = "input" });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "b", SourcePortName = "output", TargetNodeId = "c", TargetPortName = "input" });
        return doc;
    }

    [Fact]
    public void Read_AcceptsNamesOrNumbers_AndDefaultsToFail()
    {
        NodeErrorHandling.Read(null).Should().Be(NodeErrorHandling.Settings.Default);
        NodeErrorHandling.Read(Json("""{ "onErrorBehavior": "useErrorHandler", "errorNodeId": "h", "maxConsecutiveErrors": 3 }"""))
            .Should().Be(new NodeErrorHandling.Settings(NodeErrorHandling.UseErrorHandler, "h", 3));
        NodeErrorHandling.Read(Json("""{ "OnErrorBehavior": 3 }""")).Behavior.Should().Be(NodeErrorHandling.Retry, because: "the engine's enum order is Fail, Continue, UseErrorHandler, Retry");
    }

    [Fact]
    public void Write_RoundTrips_AndDropsHandlerFieldsForOtherBehaviors()
    {
        var settings = new NodeErrorHandling.Settings(NodeErrorHandling.UseErrorHandler, "h", 2);
        NodeErrorHandling.Read(NodeErrorHandling.Write(settings)).Should().Be(settings);

        NodeErrorHandling.Write(NodeErrorHandling.Settings.Default).Should().BeNull();
        NodeErrorHandling.Read(NodeErrorHandling.Write(new NodeErrorHandling.Settings(NodeErrorHandling.Continue, "h", 2)))
            .Should().Be(new NodeErrorHandling.Settings(NodeErrorHandling.Continue));
    }

    [Fact]
    public void HandlerCandidates_ExcludeTheNodeAndEverythingDownstreamOfIt()
    {
        var doc = Doc();

        NodeErrorHandling.HandlerCandidates(doc, doc.FindNode("b")!).Select(n => n.Id).Should().BeEquivalentTo("a", "h");
    }

    [Theory]
    [InlineData(null, "no handler node is chosen")]
    [InlineData("b", "can't be its own error handler")]
    [InlineData("ghost", "isn't in this workflow")]
    [InlineData("c", "downstream of it")]
    public void Lint_FlagsABadHandler(string? handler, string expected)
    {
        var doc = Doc();
        doc.FindNode("b")!.ErrorHandling = NodeErrorHandling.Write(new NodeErrorHandling.Settings(NodeErrorHandling.UseErrorHandler, handler));

        NodeErrorHandling.Validate(doc).Should().ContainSingle()
            .Which.Should().Match<GraphIssue>(i => i.Severity == IssueSeverity.Error && i.NodeId == "b" && i.Message.Contains(expected));
    }

    [Fact]
    public void Lint_AcceptsAGoodHandler_AndShowsTheLink()
    {
        var doc = Doc();
        doc.FindNode("b")!.ErrorHandling = NodeErrorHandling.Write(new NodeErrorHandling.Settings(NodeErrorHandling.UseErrorHandler, "h"));

        NodeErrorHandling.Validate(doc).Should().BeEmpty();
        GraphValidator.ValidateStartAndEnd(doc).Should().BeEmpty();
        NodeErrorHandling.Links(doc).Should().Equal(new NodeErrorHandling.Link("b", "h"));
    }

    [Fact]
    public void Lint_FlagsAMissingWorkflowLevelHandler()
    {
        var doc = Doc();
        doc.ErrorHandling = NodeErrorHandling.Write(new NodeErrorHandling.Settings(NodeErrorHandling.UseErrorHandler, "ghost"));

        NodeErrorHandling.Validate(doc).Should().ContainSingle().Which.Message.Should().Contain("handler node is missing");
    }
}
