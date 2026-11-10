// <copyright file="RunStateTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Execution.State;

using System.Collections.Generic;
using FluentAssertions;
using Workflow.UI.Client.Execution.State;
using Xunit;

/// <summary>
/// 🧪 Phase 3.3.c.0/c.1 — Run-state event mapping + CSS painting specs~ ✨.
/// </summary>
public sealed class RunStateTests
{
    [Fact]
    public void Snapshot_SeedsNodeStates()
    {
        var run = new RunState();
        run.SeedFromSnapshot("Running", 40, new Dictionary<string, string> { ["a"] = "Completed", ["b"] = "Running" }, null);

        run.Nodes["a"].State.Should().Be(NodeRunState.Completed);
        run.Nodes["b"].State.Should().Be(NodeRunState.Running);
        run.Percentage.Should().Be(40);
    }

    [Fact]
    public void NodeStarted_PaintsRunning()
    {
        var run = new RunState();
        run.NodeStarted("a");

        run.Nodes["a"].State.Should().Be(NodeRunState.Running);
        run.CssClassFor("a").Should().Be("df-node--running");
        run.CurrentNode.Should().Be("a");
    }

    [Fact]
    public void NodeCompleted_PaintsCompleted_WithDuration()
    {
        var run = new RunState();
        run.NodeCompleted("a", 123);

        run.Nodes["a"].State.Should().Be(NodeRunState.Completed);
        run.Nodes["a"].DurationMs.Should().Be(123);
        run.CssClassFor("a").Should().Be("df-node--completed");
    }

    [Fact]
    public void NodeFailed_PaintsFailed_WithError()
    {
        var run = new RunState();
        run.NodeFailed("a", "boom", 50);

        run.Nodes["a"].State.Should().Be(NodeRunState.Failed);
        run.Nodes["a"].Error.Should().Be("boom");
        run.CssClassFor("a").Should().Be("df-node--failed");
    }

    [Fact]
    public void Progress_UpdatesBarAndCounts()
    {
        var run = new RunState();
        run.Progress(60, "b", 3, 5);

        run.Percentage.Should().Be(60);
        run.CurrentNode.Should().Be("b");
        run.CompletedNodes.Should().Be(3);
        run.TotalNodes.Should().Be(5);
    }

    [Fact]
    public void MarkCompleted_SetsTerminal()
    {
        var run = new RunState();
        run.MarkCompleted(999);

        run.Overall.Should().Be("Completed");
        run.Percentage.Should().Be(100);
        run.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void MarkFailed_SetsError()
    {
        var run = new RunState();
        run.MarkFailed("nope", 10);

        run.Overall.Should().Be("Failed");
        run.Error.Should().Be("nope");
        run.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void Log_AppendsInOrder()
    {
        var run = new RunState();
        run.AddLog("one");
        run.AddLog("two");

        run.Log.Should().HaveCount(2);
        run.Log[0].Text.Should().Be("one");
        run.Log[1].Text.Should().Be("two");
    }

    [Fact]
    public void CssClassFor_UnknownNode_Empty()
    {
        new RunState().CssClassFor("ghost").Should().BeEmpty();
    }

    [Fact]
    public void Changed_Fires_OnEvents()
    {
        var run = new RunState();
        var fired = 0;
        run.Changed += () => fired++;

        run.NodeStarted("a");

        fired.Should().BeGreaterThan(0);
    }

    /// <summary>📡 Stage 7.6 — live counts, then the final ones; a late live tick doesn't undo the final.</summary>
    [Fact]
    public void StreamProgress_TracksLiveThenFinalCounts()
    {
        var id = System.Guid.NewGuid();
        var run = new RunState { ExecutionId = id };

        run.StreamProgress(id, 0, new Dictionary<string, long> { ["src"] = 1234, ["sink"] = 1200 }, 1500, completed: false);
        run.StreamCounts["src"].Should().Be(new StreamStageCount(0, 1234, 1500, false));
        run.ItemBadgeFor("src").Should().Be(1234.ToString("N0") + " items");
        run.ItemBadgeFor("other").Should().BeNull();

        run.StreamProgress(id, 0, new Dictionary<string, long> { ["src"] = 2000, ["sink"] = 2000 }, 2600, completed: true);
        run.StreamProgress(id, 0, new Dictionary<string, long> { ["src"] = 1999, ["sink"] = 1990 }, 2500, completed: false);

        run.StreamCounts["src"].Should().Be(new StreamStageCount(0, 2000, 2600, true));
        run.StreamCounts["sink"].Items.Should().Be(2000);
        run.Log.Should().Contain(l => l.Text.Contains("streaming region 0 finished"));
    }

    /// <summary>📡 Stage 7.6 — counts for another execution are ignored; one item is singular.</summary>
    [Fact]
    public void StreamProgress_IgnoresOtherExecutions()
    {
        var run = new RunState { ExecutionId = System.Guid.NewGuid() };

        run.StreamProgress(System.Guid.NewGuid(), 0, new Dictionary<string, long> { ["src"] = 5 }, 10, completed: false);
        run.StreamCounts.Should().BeEmpty();

        run.StreamProgress(run.ExecutionId, 0, new Dictionary<string, long> { ["src"] = 1 }, 10, completed: false);
        run.ItemBadgeFor("src").Should().Be("1 item");
    }

    /// <summary>Follow-up: the user's own cancel and the server's ExecutionCancelled event log it once.</summary>
    [Fact]
    public void MarkCancelled_Twice_LogsOnce()
    {
        var run = new RunState();

        run.MarkCancelled();
        run.MarkCancelled();

        run.Overall.Should().Be("Cancelled");
        run.Log.Count(l => l.Text == "execution cancelled").Should().Be(1);
    }
}
