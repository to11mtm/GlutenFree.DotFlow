// <copyright file="JoinAfterSkippedBranchTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

using System;
using System.Collections.Generic;
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
/// 01 §12.6 (refuted, kept as a regression guard) — a join still runs when one input completed and
/// the other branch was skipped.
/// </summary>
/// <remarks>
/// Graph: <c>branch</c> activates only "a"; "a" → <c>fast</c> → <c>join</c>; branch "b" →
/// <c>slow</c> → <c>join</c>. The "a" path completes first, satisfying one join predecessor, but
/// the "b" path is on a deactivated port and its skip propagates to the shared join. The skip rule
/// treats the already-completed "a" predecessor as settled, so the join can be skipped instead of
/// running once its live predecessor arrives. The join SHOULD run.
/// This is an in-memory actor test; stubs only.
/// </remarks>
public sealed class JoinAfterSkippedBranchTests : TestKit
{
    [Fact]
    public void Join_WithOneCompletedInput_ShouldRun_NotBeSkipped()
    {
        var branch = new PortModule("f.jbranch", activePorts: new[] { "a" }, declaredPorts: new[] { "a", "b" });
        var fast = new PassModule("f.jfast");
        var slow = new PassModule("f.jslow", delayMs: 300);
        var join = new PassModule("f.jjoin");
        var services = FindingDefs.Services(new Workflow.Modules.Abstractions.IWorkflowModule[] { branch, fast, slow, join });

        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("branch", "f.jbranch"),
                FindingDefs.Node("fast", "f.jfast"),
                FindingDefs.Node("slow", "f.jslow"),
                FindingDefs.Node("join", "f.jjoin"),
            },
            connections: new[]
            {
                FindingDefs.Edge("branch", "a", "fast", "input"),
                FindingDefs.Edge("branch", "b", "slow", "input"),
                FindingDefs.Edge("fast", "output", "join", "input"),
                FindingDefs.Edge("slow", "output", "join", "input"),
            });

        var executionId = Guid.NewGuid();
        var parent = this.CreateTestProbe("join-skip-parent");
        var executor = parent.ChildActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), services),
            "join-skip-exec");
        executor.Tell(new StartExecution(executionId));

        parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, FindingDefs.RunTimeout);

        join.Calls.Should().Be(
            1,
            because: "the join has a completed 'fast' input, so it should run rather than be skipped by the 'b'-branch deactivation (01 §12.6)");
    }
}
