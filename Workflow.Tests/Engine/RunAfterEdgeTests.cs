// <copyright file="RunAfterEdgeTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Flow;
using Workflow.Tests.Findings;
using Xunit;

/// <summary>
/// Plan 09 — a run-after edge sequences without delivering data: the target waits for the source
/// port (and is skipped with it) like any edge, but nothing lands in its inputs, and a FanIn waits
/// for it without adding a branch.
/// </summary>
public sealed class RunAfterEdgeTests : TestKit
{
    private static ConnectionDefinition After(string from, string fromPort, string to)
        => new(from, fromPort, to, string.Empty, Kind: ConnectionKind.RunAfter);

    [Fact]
    public void RunAfter_RunsTheTargetAfterTheSource_WithoutDeliveringData()
    {
        var src = new OutputModule("ra.src", new Dictionary<string, object?> { ["output"] = "hello" });
        var target = new CaptureModule("ra.target", inputPorts: new[] { "value" }, templatedProperties: new[] { "p" });
        var services = FindingDefs.Services(new IWorkflowModule[] { src, target });

        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("src", "ra.src"),
                FindingDefs.Node("target", "ra.target", new (string, object?)[] { ("p", "{{src.output}}") }),
            },
            connections: new[] { After("src", "output", "target") });

        this.Run(definition, services).Should().BeOfType<WorkflowCompleted>();

        target.Captured.Should().ContainSingle();
        var call = target.Captured.Single();
        call.Properties["p"].Should().Be("hello", "the target ran after the source, so {{src.output}} resolves");
        // The binder lists every declared port (null when nothing arrived), so look at values.
        call.Inputs.Where(kv => !kv.Key.StartsWith("__", StringComparison.Ordinal) && kv.Value is not null)
            .Should().BeEmpty("a run-after edge delivers no data (no port value, no src.* copies)");
    }

    [Fact]
    public void RunAfter_FromAnInactivePort_SkipsTheTarget()
    {
        var branch = new PortModule("ra.branch", activePorts: new[] { "a" }, declaredPorts: new[] { "a", "b" });
        var onA = new CaptureModule("ra.onA", inputPorts: new[] { "value" });
        var onB = new CaptureModule("ra.onB", inputPorts: new[] { "value" });
        var services = FindingDefs.Services(new IWorkflowModule[] { branch, onA, onB });

        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("branch", "ra.branch"),
                FindingDefs.Node("onA", "ra.onA"),
                FindingDefs.Node("onB", "ra.onB"),
            },
            connections: new[] { After("branch", "a", "onA"), After("branch", "b", "onB") });

        this.Run(definition, services).Should().BeOfType<WorkflowCompleted>();

        onA.Calls.Should().Be(1, "port routing still applies to run-after edges");
        onB.Calls.Should().Be(0, "its source port was not activated");
    }

    [Fact]
    public void RunAfter_InALoopBody_RunsPerItem_WithoutDeliveringTheItemAsAnInput()
    {
        var loop = new LoopModule("ra.loop", new object?[] { 1, 2, 3 });
        var body = new CaptureModule("ra.body", inputPorts: new[] { "value" });
        var services = FindingDefs.Services(new IWorkflowModule[] { loop, body });

        var definition = FindingDefs.Definition(
            nodes: new[] { FindingDefs.Node("loop", "ra.loop"), FindingDefs.Node("body", "ra.body") },
            connections: new[] { After("loop", "loopBody", "body") });

        this.Run(definition, services).Should().BeOfType<WorkflowCompleted>();

        body.Calls.Should().Be(3, "a run-after edge from loopBody still defines the body");
        body.Captured.Select(c => c.Variables.TryGetValue("item", out var item) ? Convert.ToInt32(item) : -1)
            .Should().BeEquivalentTo(new[] { 1, 2, 3 }, "the body still sees item as a body variable");
        body.Captured.Should().OnlyContain(
            c => c.Inputs.GetValueOrDefault("value") == null && !c.Inputs.ContainsKey(string.Empty),
            "the item arrives as a body variable, not on a port");
    }

    [Fact]
    public void RunAfter_BetweenBodyNodes_DeliversNoData()
    {
        // Inside a body the sub-graph executor gathers inputs, so check it there too.
        var loop = new LoopModule("ra.loop2", new object?[] { 1, 2 });
        var first = new OutputModule("ra.first", new Dictionary<string, object?> { ["output"] = "from-first" });
        var second = new CaptureModule("ra.second", inputPorts: new[] { "value" }, templatedProperties: new[] { "p" });
        var services = FindingDefs.Services(new IWorkflowModule[] { loop, first, second });

        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("loop", "ra.loop2"),
                FindingDefs.Node("first", "ra.first"),
                FindingDefs.Node("second", "ra.second", new (string, object?)[] { ("p", "{{first.output}}") }),
            },
            connections: new[] { After("loop", "loopBody", "first"), After("first", "output", "second") });

        this.Run(definition, services).Should().BeOfType<WorkflowCompleted>();

        second.Calls.Should().Be(2);
        second.Captured.Should().OnlyContain(c => Equals(c.Properties["p"], "from-first"), "it ran after 'first' in each iteration");
        // A body node also sees the body inputs (item/index), so look only at what this edge would carry.
        second.Captured.Should().OnlyContain(
            c => c.Inputs.GetValueOrDefault("value") == null
                 && !c.Inputs.Keys.Any(k => k.StartsWith("first.", StringComparison.Ordinal)),
            "a run-after edge in a body delivers no data either (no port value, no first.* copies)");
    }

    [Fact]
    public void FanIn_WaitsForARunAfterEdge_ButAddsNoBranchForIt()
    {
        var a = new OutputModule("ra.a", new Dictionary<string, object?> { ["output"] = "from-a" });
        var b = new PassModule("ra.b", delayMs: 200);
        var sink = new CaptureModule("ra.sink");
        var services = FindingDefs.Services(new IWorkflowModule[] { a, b, new FanInModule(), sink });

        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("a", "ra.a"),
                FindingDefs.Node("b", "ra.b"),
                FindingDefs.Node("join", "builtin.fanin", new (string, object?)[] { ("mode", "concat") }),
                FindingDefs.Node("sink", "ra.sink"),
            },
            connections: new[]
            {
                FindingDefs.Edge("a", "output", "join", "branches"),
                After("b", "output", "join"),
                FindingDefs.Edge("join", "result", "sink", "input"),
            });

        this.Run(definition, services).Should().BeOfType<WorkflowCompleted>();

        b.Calls.Should().Be(1);
        sink.Captured.Should().ContainSingle();
        var result = sink.Captured.Single().Inputs["input"];
        result.Should().BeAssignableTo<System.Collections.IEnumerable>();
        ((System.Collections.IEnumerable)result!).Cast<object?>().Should().HaveCount(1, "only the data edge is a branch");
    }

    private object Run(WorkflowDefinition definition, IServiceProvider services)
    {
        var executionId = Guid.NewGuid();
        var parent = this.CreateTestProbe();
        var executor = parent.ChildActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), services));
        executor.Tell(new StartExecution(executionId));
        return parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, FindingDefs.RunTimeout);
    }
}
