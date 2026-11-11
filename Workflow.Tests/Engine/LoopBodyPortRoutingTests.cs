// <copyright file="LoopBodyPortRoutingTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

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
using Workflow.Tests.Findings;
using Xunit;

/// <summary>
/// A branch (port-activating) node inside a loop body routes by its active ports, like at the top
/// level. Retired from the finding tests (01 §12.4) once fixed; the assertions are unchanged.
/// </summary>
public sealed class LoopBodyPortRoutingTests : TestKit
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
}
