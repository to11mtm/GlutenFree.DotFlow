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
