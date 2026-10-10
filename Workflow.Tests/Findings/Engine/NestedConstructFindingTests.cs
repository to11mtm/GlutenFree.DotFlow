// <copyright file="NestedConstructFindingTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Findings.Engine;

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Flow;
using Xunit;

/// <summary>
/// Finding 02 §12.10 — a construct (loop, try/catch, …) nested inside another construct's body
/// should still run as a construct. Body nodes run in <see cref="SubGraphExecutor"/>, which has no
/// handlers for the <c>Node*ExecutionRequested</c> messages a construct node sends, so a nested
/// construct completes as a plain node and every outgoing edge fires once as an ordinary successor.
/// </summary>
/// <remarks>
/// Uses the real <c>builtin.loop.foreach</c> / <c>builtin.trycatch</c> modules plus stubs.
/// Safety: in-memory actors and stub modules only.
/// </remarks>
[Trait("Category", FindingDefs.Category)]
public sealed class NestedConstructFindingTests : TestKit
{
    /// <summary>
    /// foreach [1,2,3] → body: foreach [a,b] → body: leaf. The leaf should run 3 × 2 = 6 times, the
    /// inner loop's done successor once per outer iteration, and the outer done successor once.
    /// </summary>
    [Fact]
    public void ForeachNestedInForeachBody_ShouldRunInnerBodyItemsTimesOuterItems()
    {
        var leaf = new PassModule("f.nested.leaf");
        var innerDone = new PassModule("f.nested.innerdone");
        var after = new PassModule("f.nested.after");

        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                Foreach("outer", 1, 2, 3),
                Foreach("inner", "a", "b"),
                FindingDefs.Node("leaf", "f.nested.leaf"),
                FindingDefs.Node("innerDone", "f.nested.innerdone"),
                FindingDefs.Node("after", "f.nested.after"),
            },
            connections: new[]
            {
                FindingDefs.Edge("outer", "loopBody", "inner", "input"),
                FindingDefs.Edge("inner", "loopBody", "leaf", "input"),
                FindingDefs.Edge("inner", "done", "innerDone", "input"),
                FindingDefs.Edge("outer", "done", "after", "input"),
            });

        var terminal = this.Run(definition, new ForEachModule(), leaf, innerDone, after);

        terminal.Should().BeOfType<WorkflowCompleted>();
        leaf.Calls.Should().Be(6, because: "the inner loop should iterate its 2 items in each of the 3 outer iterations (02 §12.10)");
        innerDone.Calls.Should().Be(3, because: "the inner loop's done port should fire once per outer iteration");
        after.Calls.Should().Be(1);
    }

    /// <summary>
    /// foreach [1,2,3] → body: trycatch (try: always-failing node, catch: handler). Each iteration's
    /// failure should be caught, so the handler runs 3 times and the loop (and run) completes.
    /// </summary>
    [Fact]
    public void TryCatchInsideForeachBody_ShouldCatchPerIteration_AndLoopCompletes()
    {
        var boom = new FlakyModule("f.nested.boom", failTimes: int.MaxValue);
        var handler = new PassModule("f.nested.handler");
        var after = new PassModule("f.nested.aftertc");

        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                Foreach("loop", 1, 2, 3),
                FindingDefs.Node("tc", "builtin.trycatch"),
                FindingDefs.Node("boom", "f.nested.boom"),
                FindingDefs.Node("handler", "f.nested.handler"),
                FindingDefs.Node("after", "f.nested.aftertc"),
            },
            connections: new[]
            {
                FindingDefs.Edge("loop", "loopBody", "tc", "input"),
                FindingDefs.Edge("tc", "try", "boom", "input"),
                FindingDefs.Edge("tc", "catch", "handler", "input"),
                FindingDefs.Edge("loop", "done", "after", "input"),
            });

        var terminal = this.Run(definition, new ForEachModule(), new TryCatchModule(), boom, handler, after);

        terminal.Should().BeOfType<WorkflowCompleted>(
            because: "the try/catch inside the body should catch each iteration's failure, so the loop completes (02 §12.10)");
        boom.Calls.Should().Be(3);
        handler.Calls.Should().Be(3, because: "the catch body should run once per caught failure");
        after.Calls.Should().Be(1);
    }

    private static NodeDefinition Foreach(string id, params object?[] items)
        => FindingDefs.Node(id, "builtin.loop.foreach", properties: new (string, object?)[] { ("collection", items) });

    private object Run(WorkflowDefinition definition, params IWorkflowModule[] modules)
    {
        var executionId = Guid.NewGuid();
        var parent = this.CreateTestProbe();
        var executor = parent.ChildActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), FindingDefs.Services(modules)));
        executor.Tell(new StartExecution(executionId));
        return parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, FindingDefs.RunTimeout);
    }
}
