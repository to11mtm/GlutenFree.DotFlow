// <copyright file="EngineFindingTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Findings.Engine;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Xunit;

/// <summary>
/// Tests for the engine findings in <c>docs/architecture/01-execution-lifecycle.md §12</c> and
/// the summary table in <c>docs/architecture/README.md</c>.
/// </summary>
/// <remarks>
/// Each test asserts the behaviour the engine SHOULD have, so a failure confirms the finding is
/// real and the test turns green once it is fixed. All are tagged
/// <c>[Trait("Category","Finding")]</c> so CI can exclude them with
/// <c>--filter Category!=Finding</c> until the fixes land.
/// Safety: in-memory actors and stub modules only — no file, network, or process access.
/// </remarks>
[Trait("Category", FindingDefs.Category)]
public sealed class EngineFindingTests : TestKit
{
    private (IActorRef Executor, TestProbe Parent, Guid ExecutionId) StartRun(
        WorkflowDefinition definition,
        IServiceProvider services,
        [System.Runtime.CompilerServices.CallerMemberName] string name = "run")
    {
        var executionId = Guid.NewGuid();
        var parent = this.CreateTestProbe(name + "-parent");
        var executor = parent.ChildActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), services),
            name + "-exec");
        executor.Tell(new StartExecution(executionId));
        return (executor, parent, executionId);
    }

    // ── Finding 01 §12.2 — executor actors are never stopped ─────────────────────────────────────

    /// <summary>
    /// After a run reaches a terminal state, its <c>WorkflowExecutor</c> should stop, so the actor
    /// tree and its captured inputs/outputs don't accumulate for the process lifetime.
    /// </summary>
    [Fact]
    public void CompletedRun_ShouldStopExecutorActor()
    {
        var services = FindingDefs.Services(new[] { new PassModule("f.pass") });
        var definition = FindingDefs.Definition(new[] { FindingDefs.Node("n1", "f.pass") });

        var (executor, parent, _) = this.StartRun(definition, services);

        // Wait for terminal completion first.
        parent.FishForMessage(m => m is WorkflowCompleted, FindingDefs.RunTimeout);

        // The executor should terminate itself shortly after completing.
        this.Watch(executor);
        this.ExpectTerminated(executor, TimeSpan.FromSeconds(5))
            .Should().NotBeNull(because: "a finished executor should stop so runs don't leak actors/memory (01 §12.2)");
    }

    // ── Finding 01 §12.3 — cancel should cancel the whole run, including construct actors ─────────

    /// <summary>
    /// Cancelling a run whose loop body is still iterating should stop further iterations promptly.
    /// The loop body module counts its calls; after cancel the count must stop climbing.
    /// </summary>
    [Fact]
    public void Cancel_ShouldStopRunningLoopIterations()
    {
        var body = new SlowCounterModule("f.slowbody", delayMs: 300);
        var loop = new LoopModule("f.loop", items: Enumerable.Range(0, 50).Cast<object?>().ToList());
        var services = FindingDefs.Services(new Workflow.Modules.Abstractions.IWorkflowModule[] { loop, body });

        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("loop", "f.loop"),
                FindingDefs.Node("bodyNode", "f.slowbody"),
            },
            connections: new[] { FindingDefs.Edge("loop", "loopBody", "bodyNode", "input") });

        var (executor, _, executionId) = this.StartRun(definition, services);

        // Let a few iterations run, then cancel.
        Thread.Sleep(800);
        executor.Tell(new CancelExecution(executionId));
        Thread.Sleep(300);
        var atCancel = body.Calls;

        // After cancel, no meaningful number of further iterations should run.
        Thread.Sleep(1500);
        var afterWait = body.Calls;

        afterWait.Should().BeLessThanOrEqualTo(
            atCancel + 1,
            because: "cancelling a run must stop in-flight loop iterations, not let all 50 run (01 §12.3)");
    }

    // ── Finding 01 §12.4a — a node inside a loop body should honour its Retry policy ──────────────

    /// <summary>
    /// A body node configured with <c>ErrorBehavior.Retry</c> that fails once then succeeds should
    /// be retried inside the loop body, so the run completes. Today <c>SubGraphExecutor</c> fails
    /// the sub-graph on the first node failure regardless of the node's error handling.
    /// </summary>
    [Fact]
    public void RetryInsideLoopBody_ShouldBeHonoured()
    {
        var loop = new LoopModule("f.loop2", items: new List<object?> { 1 });
        var flaky = new FlakyModule("f.flaky", failTimes: 1);
        var services = FindingDefs.Services(new Workflow.Modules.Abstractions.IWorkflowModule[] { loop, flaky });

        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("loop", "f.loop2"),
                FindingDefs.Node(
                    "bodyNode",
                    "f.flaky",
                    errorHandling: new ErrorHandling(ErrorBehavior.Retry),
                    retry: new RetryPolicy(MaxAttempts: 3, DelayMs: 10, BackoffMultiplier: 1.0, MaxDelayMs: 50)),
            },
            connections: new[] { FindingDefs.Edge("loop", "loopBody", "bodyNode", "input") });

        var (_, parent, _) = this.StartRun(definition, services);

        var terminal = parent.FishForMessage(
            m => m is WorkflowCompleted or WorkflowFailed, FindingDefs.RunTimeout);

        terminal.Should().BeOfType<WorkflowCompleted>(
            because: "a Retry-configured body node that recovers on attempt 2 should let the loop complete (01 §12.4)");
        flaky.Calls.Should().BeGreaterThanOrEqualTo(2, because: "the body node should have been retried");
    }

    // ── Finding 01 §12.4b — a construct nested inside a loop body should still run as a construct ──

    /// <summary>
    /// A branch (port-activating) node inside a loop body should route by its active ports: the
    /// deactivated branch must be skipped. If body sub-graphs don't honour port routing, both
    /// branches would run.
    /// </summary>
    [Fact]
    public void PortRoutingInsideLoopBody_ShouldSkipDeactivatedBranch()
    {
        var loop = new LoopModule("f.loop3", items: new List<object?> { 1 });
        var branch = new PortModule("f.branch", activePorts: new[] { "true" }, declaredPorts: new[] { "true", "false" });
        var onTrue = new PassModule("f.true");
        var onFalse = new PassModule("f.false");
        var services = FindingDefs.Services(new Workflow.Modules.Abstractions.IWorkflowModule[] { loop, branch, onTrue, onFalse });

        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("loop", "f.loop3"),
                FindingDefs.Node("branch", "f.branch"),
                FindingDefs.Node("t", "f.true"),
                FindingDefs.Node("fNode", "f.false"),
            },
            connections: new[]
            {
                FindingDefs.Edge("loop", "loopBody", "branch", "input"),
                FindingDefs.Edge("branch", "true", "t", "input"),
                FindingDefs.Edge("branch", "false", "fNode", "input"),
            });

        var (_, parent, _) = this.StartRun(definition, services);
        parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, FindingDefs.RunTimeout);

        onTrue.Calls.Should().Be(1, because: "the active 'true' branch inside the body should run");
        onFalse.Calls.Should().Be(0, because: "the deactivated 'false' branch inside the body should be skipped (01 §12.4)");
    }

    // ── Finding 01 §12.7 — UseErrorHandler is not implemented ─────────────────────────────────────

    /// <summary>
    /// A node with <c>ErrorBehavior.UseErrorHandler</c> should route to its configured error node
    /// rather than failing the whole workflow. Today the behaviour logs a warning and fails.
    /// </summary>
    [Fact]
    public void UseErrorHandler_ShouldRouteToHandler_NotFailWorkflow()
    {
        var failing = new FlakyModule("f.always", failTimes: int.MaxValue);
        var handler = new PassModule("f.handler");
        var services = FindingDefs.Services(new Workflow.Modules.Abstractions.IWorkflowModule[] { failing, handler });

        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("boom", "f.always", errorHandling: new ErrorHandling(ErrorBehavior.UseErrorHandler, ErrorNodeId: "handler")),
                FindingDefs.Node("handler", "f.handler"),
            });

        var (_, parent, _) = this.StartRun(definition, services);
        var terminal = parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, FindingDefs.RunTimeout);

        terminal.Should().BeOfType<WorkflowCompleted>(
            because: "UseErrorHandler should divert to the error node instead of failing the run (01 §12.7)");
        handler.Calls.Should().Be(1, because: "the configured error handler node should run");
    }
}
