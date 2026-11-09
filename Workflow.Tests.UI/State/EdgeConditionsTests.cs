// <copyright file="EdgeConditionsTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using System.Collections.Generic;
using System.Linq;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>Stage 7.2 — conditional edges in the designer: labels, checks, and the panel's Priority field.</summary>
public sealed class EdgeConditionsTests : TestContext
{
    public EdgeConditionsTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new Workflow.UI.Client.Scripts.State.ScriptStudioHandoff());
        this.Services.AddSingleton(new Workflow.UI.Client.Linq.State.LinqStudioHandoff());
    }

    private static DesignerDocument Doc(string sourceModule = "m")
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "src", ModuleId = sourceModule, Name = "Src" });
        foreach (var id in new[] { "a", "b", "c" })
        {
            doc.Nodes.Add(new DesignerNode { Id = id, ModuleId = "m", Name = id.ToUpperInvariant() });
        }

        return doc;
    }

    private static DesignerConnection Edge(string to, string? condition = null, int priority = 0, string port = "output")
        => new() { SourceNodeId = "src", SourcePortName = port, TargetNodeId = to, TargetPortName = "input", Condition = condition, Priority = priority };

    [Theory]
    [InlineData(null, 0, null)]
    [InlineData("value > 1", 0, "value > 1")]
    [InlineData("value > 1", 2, "2: value > 1")]
    [InlineData(null, 3, "3: else")]
    public void Label_ShowsTheConditionAndPriority(string? condition, int priority, string? expected)
        => EdgeConditions.Label(Edge("a", condition, priority)).Should().Be(expected);

    [Fact]
    public void AnElseEdgeThatIsntLast_HidesTheEdgesAfterIt()
    {
        var doc = Doc();
        doc.Connections.Add(Edge("a", "value > 10", 1));
        doc.Connections.Add(Edge("b", null, 2));
        doc.Connections.Add(Edge("c", "value > 1", 3));

        EdgeConditions.Validate(doc).Should().ContainSingle()
            .Which.Message.Should().Contain("Priority 2 has no condition").And.Contain("(C) can never fire");
    }

    [Fact]
    public void AWellFormedChain_IsClean()
    {
        var doc = Doc();
        doc.Connections.Add(Edge("a", "value > 10", 1));
        doc.Connections.Add(Edge("b", "value > 1", 2));
        doc.Connections.Add(Edge("c", null, 3));

        EdgeConditions.Validate(doc).Should().BeEmpty();
    }

    [Fact]
    public void AConditionNamingAMissingNode_IsAnError_ButKnownRootsAreFine()
    {
        var doc = Doc();
        doc.Connections.Add(Edge("a", "ghost.count > 1 && Math.max(value, 1) > 0 && a.output != null && Variable.x"));

        EdgeConditions.Validate(doc).Should().ContainSingle()
            .Which.Should().Match<GraphIssue>(i => i.Severity == IssueSeverity.Error && i.Message.Contains("'ghost'"));
    }

    [Fact]
    public void AConditionOnAConstructsEdge_IsFlaggedAsIgnored()
    {
        var doc = Doc("builtin.loop.foreach");
        doc.Connections.Add(Edge("a", "value > 1", port: "done"));

        EdgeConditions.Validate(doc).Should().ContainSingle().Which.Message.Should().Contain("routes its own edges");
    }

    [Fact]
    public void ThePanel_SetsAndClearsPriority_Undoably_AndExplainsValue()
    {
        var doc = Doc();
        var edge = Edge("a", "value > 1");
        doc.Connections.Add(edge);
        var sel = new SelectionState();
        sel.SelectConnection(edge.Key);
        var cmd = new CommandStack(doc);
        var cut = this.RenderComponent<PropertiesPanel>(p => p.Add(x => x.Document, doc).Add(x => x.Selection, sel).Add(x => x.Commands, cmd));

        cut.Find("[data-testid=conn-condition-help]").TextContent.Should().Contain("value");
        cut.Find("[data-testid=conn-priority]").Change("2");
        cut.Find("[data-testid=conn-apply]").Click();
        doc.Connections.Single().Priority.Should().Be(2);

        cmd.Undo();
        doc.Connections.Single().Priority.Should().Be(0);
    }
}
