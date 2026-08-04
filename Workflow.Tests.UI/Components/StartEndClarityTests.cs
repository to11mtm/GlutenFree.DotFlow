// <copyright file="StartEndClarityTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Components;

using System.Collections.Generic;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// 🧪 Start/End clarity C2/C3/C4/C5 — bUnit specs for the canvas role badges, ambiguity
/// presentation, catalog icons, issue→node highlighting, and the StatusBar summary~ 🧭.
/// </summary>
public sealed class StartEndClarityTests : TestContext
{
    public StartEndClarityTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new Workflow.UI.Client.Services.PaletteDragState());
    }

    private static DesignerNode Node(string id, string moduleId = "m", double x = 0, double y = 0)
        => new() { Id = id, ModuleId = moduleId, Name = id, X = x, Y = y };

    private static DesignerConnection Conn(string s, string t)
        => new() { SourceNodeId = s, SourcePortName = "output", TargetNodeId = t, TargetPortName = "input" };

    private static DesignerDocument Doc(
        IEnumerable<DesignerNode> nodes,
        IEnumerable<DesignerConnection>? conns = null)
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.AddRange(nodes);
        doc.Connections.AddRange(conns ?? []);
        return doc;
    }

    // ─── C2 — role badges + classes ─────────────────────────────────────────────

    [Fact]
    public void LinearChain_MarksStartAndEndNodes()
    {
        var doc = Doc([Node("a", x: 0), Node("b", x: 300), Node("c", x: 600)], [Conn("a", "b"), Conn("b", "c")]);

        var cut = this.RenderComponent<CanvasView>(p => p.Add(x => x.Document, doc));

        var start = cut.Find("[data-node-id=a]");
        start.ClassList.Should().Contain("df-node--role-start");
        start.QuerySelector(".df-badge--start")!.TextContent.Should().Contain("Start");

        var end = cut.Find("[data-node-id=c]");
        end.ClassList.Should().Contain("df-node--role-end");
        end.QuerySelector(".df-badge--end")!.TextContent.Should().Contain("End");

        var middle = cut.Find("[data-node-id=b]");
        middle.ClassList.Should().NotContain("df-node--role-start").And.NotContain("df-node--role-end");
        middle.QuerySelectorAll(".df-badge").Should().BeEmpty();
    }

    [Fact]
    public void SingleStart_BadgeHasNoCount()
    {
        var doc = Doc([Node("a"), Node("b")], [Conn("a", "b")]);

        var cut = this.RenderComponent<CanvasView>(p => p.Add(x => x.Document, doc));

        cut.Find("[data-node-id=a] .df-badge--start").TextContent.Trim().Should().Be("▶ Start");
    }

    [Fact]
    public void IsolatedNode_GetsUnwiredTreatment_NotBadges()
    {
        var doc = Doc([Node("a"), Node("b"), Node("lonely")], [Conn("a", "b")]);

        var cut = this.RenderComponent<CanvasView>(p => p.Add(x => x.Document, doc));

        var lonely = cut.Find("[data-node-id=lonely]");
        lonely.ClassList.Should().Contain("df-node--unwired");
        lonely.QuerySelectorAll(".df-badge").Should().BeEmpty();
    }

    // ─── C3 — ambiguity ─────────────────────────────────────────────────────────

    [Fact]
    public void MultipleStarts_BadgesRead_NofM_AndAreMarkedAmbiguous()
    {
        var doc = Doc(
            [Node("s1"), Node("s2"), Node("join")],
            [Conn("s1", "join"), Conn("s2", "join")]);

        var cut = this.RenderComponent<CanvasView>(p => p.Add(x => x.Document, doc));

        cut.Find("[data-node-id=s1] .df-badge--start").TextContent.Should().Contain("Start 1 of 2");
        cut.Find("[data-node-id=s2] .df-badge--start").TextContent.Should().Contain("Start 2 of 2");
        cut.Find("[data-node-id=s1]").ClassList.Should().Contain("df-node--role-ambiguous");
        cut.Find("[data-node-id=s1] .df-badge--start").ClassList.Should().Contain("df-badge--plural");
    }

    [Fact]
    public void MultipleEnds_BadgesRead_NofM()
    {
        var doc = Doc(
            [Node("a"), Node("e1"), Node("e2")],
            [Conn("a", "e1"), Conn("a", "e2")]);

        var cut = this.RenderComponent<CanvasView>(p => p.Add(x => x.Document, doc));

        cut.Find("[data-node-id=e1] .df-badge--end").TextContent.Should().Contain("End 1 of 2");
        cut.Find("[data-node-id=e2] .df-badge--end").TextContent.Should().Contain("End 2 of 2");
    }

    // ─── C4 — catalog icons on the canvas ───────────────────────────────────────

    [Fact]
    public void StartModule_ShowsItsDeclaredIcon_OnTheCanvas()
    {
        // Regression for F3: NodeView's private icon table missed builtin.start/end (rendered ⚙️).
        var doc = Doc([Node("s", "builtin.start"), Node("e", "builtin.end")], [Conn("s", "e")]);

        var cut = this.RenderComponent<CanvasView>(p => p.Add(x => x.Document, doc));

        cut.Find("[data-node-id=s] .df-node__icon").TextContent.Should().Be("🚀");
        cut.Find("[data-node-id=e] .df-node__icon").TextContent.Should().Be("🏁");
    }

    [Fact]
    public void ModuleIconResolver_WinsOverTheFallbackTable()
    {
        var doc = Doc([Node("n", "custom.thing")]);

        var cut = this.RenderComponent<CanvasView>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ModuleIcon, _ => "🦄"));

        cut.Find("[data-node-id=n] .df-node__icon").TextContent.Should().Be("🦄");
    }

    // ─── C5 — issue→node highlighting ───────────────────────────────────────────

    [Fact]
    public void IssueWithNodeId_HighlightsTheNode_WithMessageTooltip()
    {
        var doc = Doc([Node("a"), Node("b")], [Conn("a", "b")]);
        var issues = new List<GraphIssue>
        {
            new(IssueSeverity.Warning, "Something looks off here.", "b"),
        };

        var cut = this.RenderComponent<CanvasView>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Issues, issues));

        var node = cut.Find("[data-node-id=b]");
        node.ClassList.Should().Contain("df-node--issue-warning");
        var marker = node.QuerySelector(".df-node__issue")!;
        marker.GetAttribute("title").Should().Contain("Something looks off here.");
    }

    [Fact]
    public void ErrorOutranksWarning_OnTheSameNode()
    {
        var doc = Doc([Node("a")]);
        var issues = new List<GraphIssue>
        {
            new(IssueSeverity.Warning, "warn", "a"),
            new(IssueSeverity.Error, "boom", "a"),
        };

        var cut = this.RenderComponent<CanvasView>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Issues, issues));

        var node = cut.Find("[data-node-id=a]");
        node.ClassList.Should().Contain("df-node--issue-error");
        node.QuerySelector(".df-node__issue")!.GetAttribute("title")
            .Should().Contain("warn").And.Contain("boom");
    }

    [Fact]
    public void IssueWithoutNodeId_HighlightsNothing()
    {
        var doc = Doc([Node("a")]);
        var issues = new List<GraphIssue> { new(IssueSeverity.Warning, "global concern") };

        var cut = this.RenderComponent<CanvasView>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Issues, issues));

        cut.FindAll(".df-node__issue").Should().BeEmpty();
    }

    // ─── C3 — StatusBar summary ─────────────────────────────────────────────────

    [Fact]
    public void StatusBar_ShowsSingularStartAndEnd()
    {
        var doc = Doc([Node("a"), Node("b")], [Conn("a", "b")]);

        var cut = this.RenderComponent<StatusBar>(p => p.Add(x => x.Document, doc));

        cut.Find("[data-testid=status-starts]").TextContent.Should().Contain("1 start").And.NotContain("parallel");
        cut.Find("[data-testid=status-ends]").TextContent.Should().Contain("1 end");
    }

    [Fact]
    public void StatusBar_ShowsPluralCounts_WithParallelNote()
    {
        var doc = Doc(
            [Node("s1"), Node("s2"), Node("e1"), Node("e2")],
            [Conn("s1", "e1"), Conn("s2", "e2")]);

        var cut = this.RenderComponent<StatusBar>(p => p.Add(x => x.Document, doc));

        cut.Find("[data-testid=status-starts]").TextContent.Should().Contain("2 starts (parallel)");
        cut.Find("[data-testid=status-ends]").TextContent.Should().Contain("2 ends");
    }

    [Fact]
    public void StatusBar_EmptyDocument_HidesTheSummary()
    {
        var cut = this.RenderComponent<StatusBar>(p => p.Add(x => x.Document, new DesignerDocument()));

        cut.FindAll("[data-testid=status-starts]").Should().BeEmpty();
    }

    [Fact]
    public void StatusBar_ClickingStarts_CyclesThroughThem()
    {
        var doc = Doc(
            [Node("s1"), Node("s2"), Node("join")],
            [Conn("s1", "join"), Conn("s2", "join")]);
        var jumps = new List<string>();

        var cut = this.RenderComponent<StatusBar>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.OnJumpTo, (string id) => jumps.Add(id)));

        cut.Find("[data-testid=status-starts]").Click();
        cut.Find("[data-testid=status-starts]").Click();
        cut.Find("[data-testid=status-starts]").Click();

        jumps.Should().Equal("s1", "s2", "s1");
    }

    [Fact]
    public void StatusBar_ClickingEnd_JumpsToIt()
    {
        var doc = Doc([Node("a"), Node("b")], [Conn("a", "b")]);
        var jumps = new List<string>();

        var cut = this.RenderComponent<StatusBar>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.OnJumpTo, (string id) => jumps.Add(id)));

        cut.Find("[data-testid=status-ends]").Click();

        jumps.Should().Equal("b");
    }
}
