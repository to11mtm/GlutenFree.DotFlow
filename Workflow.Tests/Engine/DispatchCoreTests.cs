// <copyright file="DispatchCoreTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Event;
using FluentAssertions;
using LanguageExt;
using Moq;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Xunit;

/// <summary>
/// Pure-unit regression tests for <see cref="DispatchCore"/> — the shared port-aware routing
/// helper extracted from <c>WorkflowExecutor</c> and <c>SubGraphExecutor</c> in Phase 2.2.3-followup~ 🎯✨
/// </summary>
/// <remarks>
/// CopilotNote: These tests exercise the routing logic directly (no actor system needed).
/// They act as a regression guard ensuring behaviour is identical to the original
/// copy-paste implementations — any future change to DispatchCore must not break them~ 🌸.
/// </remarks>
public class DispatchCoreTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Creates a mock <see cref="ILoggingAdapter"/> that silently swallows all calls~ 🔇</summary>
    private static ILoggingAdapter NullLogger()
    {
        var mock = new Mock<ILoggingAdapter>();
        mock.Setup(l => l.IsDebugEnabled).Returns(false);
        mock.Setup(l => l.IsInfoEnabled).Returns(false);
        mock.Setup(l => l.IsWarningEnabled).Returns(false);
        mock.Setup(l => l.IsErrorEnabled).Returns(false);
        return mock.Object;
    }

    /// <summary>
    /// Builds a minimal <see cref="WorkflowDefinition"/> containing the listed connections.
    /// Auto-generates node stubs for every node ID referenced~ 🔗.
    /// </summary>
    private static WorkflowDefinition MakeDefinition(params ConnectionDefinition[] connections)
    {
        var allNodeIds = connections
            .SelectMany(c => new[] { c.SourceNodeId, c.TargetNodeId })
            .Distinct()
            .ToArray();

        var nodes = allNodeIds
            .Select(id => new NodeDefinition(id, "stub.module", id, HashMap<string, System.Text.Json.JsonElement>.Empty))
            .ToArr();

        return new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "dispatch-core-test",
            Description: null,
            Version: new Version(1, 0),
            Nodes: nodes,
            Connections: connections.ToArr(),
            Variables: HashMap<string, VariableDefinition>.Empty);
    }

    /// <summary>
    /// Builds the successor/predecessor adjacency lists from a definition, exactly as the
    /// actors would — so the test harness mirrors real actor initialisation~ 📊.
    /// </summary>
    private static (Dictionary<string, List<string>> successors, Dictionary<string, List<string>> predecessors)
        BuildGraph(WorkflowDefinition definition)
    {
        var successors = new Dictionary<string, List<string>>();
        var predecessors = new Dictionary<string, List<string>>();

        foreach (var node in definition.Nodes)
        {
            successors[node.Id] = new List<string>();
            predecessors[node.Id] = new List<string>();
        }

        foreach (var conn in definition.Connections)
        {
            if (!successors[conn.SourceNodeId].Contains(conn.TargetNodeId))
            {
                successors[conn.SourceNodeId].Add(conn.TargetNodeId);
            }

            if (!predecessors[conn.TargetNodeId].Contains(conn.SourceNodeId))
            {
                predecessors[conn.TargetNodeId].Add(conn.SourceNodeId);
            }
        }

        return (successors, predecessors);
    }

    // ── State double helpers (mutable HashSets acting as actor state) ─────────────────────────────

    private static DispatchCore MakeCore(
        WorkflowDefinition definition,
        Dictionary<string, List<string>> successors,
        Dictionary<string, List<string>> predecessors,
        System.Collections.Generic.HashSet<string> running,
        System.Collections.Generic.HashSet<string> completed,
        System.Collections.Generic.HashSet<string> failed,
        System.Collections.Generic.HashSet<string> skipped,
        List<string> executedNodes,
        Action? afterSkipCheck = null)
    {
        return new DispatchCore(
            definition: definition,
            nodeSuccessors: successors,
            nodePredecessors: predecessors,
            isRunning: nodeId => running.Contains(nodeId),
            isCompleted: nodeId => completed.Contains(nodeId),
            isFailed: nodeId => failed.Contains(nodeId),
            isSkipped: nodeId => skipped.Contains(nodeId),
            executeNode: nodeId => executedNodes.Add(nodeId),
            markNodeSkipped: nodeId => skipped.Add(nodeId),
            checkCompletionAfterSkipPropagation: afterSkipCheck ?? (() => { }),
            log: NullLogger());
    }

    // ── ExecuteReadySuccessors tests ───────────────────────────────────────────────────────────────

    /// <summary>
    /// No ActivePorts → fire-all legacy path — every direct successor whose predecessors are
    /// satisfied should execute~ ✅.
    /// CopilotNote: Regression guard for the backwards-compat contract from Phase 2.2.0a~ 💖.
    /// </summary>
    [Fact]
    public void ExecuteReadySuccessors_NoActivePorts_FiresAllSuccessors()
    {
        // Source → A, Source → B (both fire when Source completes with no port restriction)
        var definition = MakeDefinition(
            new ConnectionDefinition("source", "out", "nodeA", "in"),
            new ConnectionDefinition("source", "out", "nodeB", "in"));

        var (successors, predecessors) = BuildGraph(definition);

        var completed = new System.Collections.Generic.HashSet<string> { "source" };
        var fired = new List<string>();

        var core = MakeCore(definition, successors, predecessors,
            running: new(), completed: completed, failed: new(), skipped: new(),
            executedNodes: fired);

        // Act — no active ports (default empty Arr)
        core.ExecuteReadySuccessors("source");

        // Assert — both successors fire
        fired.Should().BeEquivalentTo(new[] { "nodeA", "nodeB" },
            "no active-ports = fire-all legacy path~ ✅");
    }

    /// <summary>
    /// ActivePorts = ["true"] → only the true-port branch fires; false-port branch is skipped~ 🎯.
    /// </summary>
    [Fact]
    public void ExecuteReadySuccessors_WithTruePort_OnlyTrueBranchFires()
    {
        var definition = MakeDefinition(
            new ConnectionDefinition("cond", "true",  "nodeTrue",  "in"),
            new ConnectionDefinition("cond", "false", "nodeFalse", "in"));

        var (successors, predecessors) = BuildGraph(definition);

        var completed = new System.Collections.Generic.HashSet<string> { "cond" };
        var skipped = new System.Collections.Generic.HashSet<string>();
        var fired = new List<string>();

        var core = MakeCore(definition, successors, predecessors,
            running: new(), completed: completed, failed: new(), skipped: skipped,
            executedNodes: fired);

        // Act — activePorts = ["true"]
        core.ExecuteReadySuccessors("cond", new Arr<string>(new[] { "true" }));

        // Assert
        fired.Should().ContainSingle().Which.Should().Be("nodeTrue",
            "only the 'true' port branch should fire~ 🎯");
        skipped.Should().ContainSingle().Which.Should().Be("nodeFalse",
            "'nodeFalse' should be recursively skipped~ ⏭️");
    }

    /// <summary>
    /// ActivePorts = ["false"] → only the false-port branch fires~ 🎯.
    /// </summary>
    [Fact]
    public void ExecuteReadySuccessors_WithFalsePort_OnlyFalseBranchFires()
    {
        var definition = MakeDefinition(
            new ConnectionDefinition("cond", "true",  "nodeTrue",  "in"),
            new ConnectionDefinition("cond", "false", "nodeFalse", "in"));

        var (successors, predecessors) = BuildGraph(definition);

        var completed = new System.Collections.Generic.HashSet<string> { "cond" };
        var skipped = new System.Collections.Generic.HashSet<string>();
        var fired = new List<string>();

        var core = MakeCore(definition, successors, predecessors,
            running: new(), completed: completed, failed: new(), skipped: skipped,
            executedNodes: fired);

        core.ExecuteReadySuccessors("cond", new Arr<string>(new[] { "false" }));

        fired.Should().ContainSingle().Which.Should().Be("nodeFalse");
        skipped.Should().ContainSingle().Which.Should().Be("nodeTrue");
    }

    /// <summary>
    /// A node with two predecessors should NOT fire until BOTH predecessors are completed/skipped~ 🔗.
    /// </summary>
    [Fact]
    public void TryFireSuccessor_NotFiredUntilAllPredecessorsSatisfied()
    {
        // A → Join, B → Join (Join requires both A and B)
        var definition = MakeDefinition(
            new ConnectionDefinition("A", "out", "join", "inA"),
            new ConnectionDefinition("B", "out", "join", "inB"));

        var (successors, predecessors) = BuildGraph(definition);

        // Only A completed; B still pending
        var completed = new System.Collections.Generic.HashSet<string> { "A" };
        var fired = new List<string>();

        var core = MakeCore(definition, successors, predecessors,
            running: new(), completed: completed, failed: new(), skipped: new(),
            executedNodes: fired);

        core.ExecuteReadySuccessors("A");

        fired.Should().BeEmpty(
            "'join' should not fire — predecessor 'B' is still pending~ 🔗");
    }

    /// <summary>
    /// The fire-all path must not re-fire nodes already running~ ✅.
    /// </summary>
    [Fact]
    public void TryFireSuccessor_SkipsAlreadyRunningNode()
    {
        var definition = MakeDefinition(
            new ConnectionDefinition("srcA", "out", "target", "in"),
            new ConnectionDefinition("srcB", "out", "target", "in"));

        var (successors, predecessors) = BuildGraph(definition);

        var completed = new System.Collections.Generic.HashSet<string> { "srcA", "srcB" };
        var running = new System.Collections.Generic.HashSet<string> { "target" };
        var fired = new List<string>();

        var core = MakeCore(definition, successors, predecessors,
            running: running, completed: completed, failed: new(), skipped: new(),
            executedNodes: fired);

        // Even though srcA just completed and all predecessors are satisfied, 'target' is already running
        core.ExecuteReadySuccessors("srcA");

        fired.Should().BeEmpty("already-running nodes should not be re-fired~ ✅");
    }

    // ── TrySkipNodeDownstream tests ────────────────────────────────────────────────────────────────

    /// <summary>
    /// When a deactivated node has further descendants, the skip propagates recursively~ ⏭️🌊.
    /// </summary>
    [Fact]
    public void TrySkipNodeDownstream_PropagatesRecursively()
    {
        // cond → false → mid → end  (all three should be skipped when 'false' is deactivated)
        // cond → true → (separate)
        var definition = MakeDefinition(
            new ConnectionDefinition("cond",  "false", "false_node", "in"),
            new ConnectionDefinition("false_node", "out",   "mid",       "in"),
            new ConnectionDefinition("mid",         "out",   "end",       "in"),
            new ConnectionDefinition("cond",  "true",  "true_node", "in"));

        var (successors, predecessors) = BuildGraph(definition);

        var completed = new System.Collections.Generic.HashSet<string> { "cond" };
        var skipped = new System.Collections.Generic.HashSet<string>();
        var fired = new List<string>();

        var core = MakeCore(definition, successors, predecessors,
            running: new(), completed: completed, failed: new(), skipped: skipped,
            executedNodes: fired);

        // Directly skip 'false_node' branch
        core.TrySkipNodeDownstream("false_node");

        skipped.Should().BeEquivalentTo(new[] { "false_node", "mid", "end" },
            "recursive skip should propagate through the entire deactivated branch~ ⏭️🌊");
    }

    /// <summary>
    /// A converging node (two predecessors) is NOT skipped if one predecessor completes
    /// normally while only the other is skipped — classic diamond-merge case~ 🔷.
    /// </summary>
    [Fact]
    public void TrySkipNodeDownstream_ConvergingNode_NotSkippedIfOnePredecessorIsActive()
    {
        // cond → true → join, cond → false → join (diamond)
        var definition = MakeDefinition(
            new ConnectionDefinition("cond", "true",  "pathTrue",  "in"),
            new ConnectionDefinition("cond", "false", "pathFalse", "in"),
            new ConnectionDefinition("pathTrue",  "out", "join", "in"),
            new ConnectionDefinition("pathFalse", "out", "join", "in"));

        var (successors, predecessors) = BuildGraph(definition);

        var completed = new System.Collections.Generic.HashSet<string> { "cond" };
        var skipped = new System.Collections.Generic.HashSet<string> { "pathFalse" }; // one path skipped
        var fired = new List<string>();

        var core = MakeCore(definition, successors, predecessors,
            running: new(), completed: completed, failed: new(), skipped: skipped,
            executedNodes: fired);

        // Try to skip 'join' — pathTrue is still pending, so join should NOT be skipped
        core.TrySkipNodeDownstream("join");

        skipped.Should().NotContain("join",
            "'join' should not be skipped — its predecessor 'pathTrue' is still pending~ 🔷");
    }

    // ── Post-skip completion callback tests ────────────────────────────────────────────────────────

    /// <summary>
    /// After port-aware skip propagation, the <c>checkCompletionAfterSkipPropagation</c>
    /// callback is invoked exactly once, giving the owning actor a chance to detect completion~ 🎉.
    /// </summary>
    [Fact]
    public void ExecuteReadySuccessors_WithActivePorts_InvokesCompletionCheckOnce()
    {
        var definition = MakeDefinition(
            new ConnectionDefinition("cond", "true",  "nodeTrue",  "in"),
            new ConnectionDefinition("cond", "false", "nodeFalse", "in"));

        var (successors, predecessors) = BuildGraph(definition);
        var completed = new System.Collections.Generic.HashSet<string> { "cond" };
        var skipped = new System.Collections.Generic.HashSet<string>();
        var fired = new List<string>();

        var completionCheckCount = 0;

        var core = MakeCore(definition, successors, predecessors,
            running: new(), completed: completed, failed: new(), skipped: skipped,
            executedNodes: fired,
            afterSkipCheck: () => completionCheckCount++);

        core.ExecuteReadySuccessors("cond", new Arr<string>(new[] { "true" }));

        completionCheckCount.Should().Be(1,
            "completion check should fire exactly once after skip propagation~ 🎉");
    }

    /// <summary>
    /// The completion callback is NOT invoked in the fire-all (no active ports) path,
    /// since no skips occur and the actor already calls IsWorkflowComplete via its own
    /// HandleNodeExecutionCompleted logic~ ✅.
    /// </summary>
    [Fact]
    public void ExecuteReadySuccessors_NoActivePorts_DoesNotInvokeCompletionCheck()
    {
        var definition = MakeDefinition(
            new ConnectionDefinition("source", "out", "nodeA", "in"));

        var (successors, predecessors) = BuildGraph(definition);
        var completed = new System.Collections.Generic.HashSet<string> { "source" };
        var fired = new List<string>();
        var completionCheckCount = 0;

        var core = MakeCore(definition, successors, predecessors,
            running: new(), completed: completed, failed: new(), skipped: new(),
            executedNodes: fired,
            afterSkipCheck: () => completionCheckCount++);

        core.ExecuteReadySuccessors("source"); // no active ports

        completionCheckCount.Should().Be(0,
            "completion check is only needed after port-aware skip propagation, not on fire-all path~ ✅");
    }
}



