// <copyright file="RunStateTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using System.Collections.Generic;
using FluentAssertions;
using Workflow.UI.Client.Designer.State;
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
}
