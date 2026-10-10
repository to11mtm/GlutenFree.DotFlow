// <copyright file="ExecutorLifecycleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Engine.Models;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Tests.Modules;
using Xunit;

/// <summary>
/// A finished <see cref="WorkflowExecutor"/> stops itself (§12.2): once the run is
/// Completed/Failed/Cancelled, its persistence writes are acknowledged, and the
/// <see cref="ExecutorLifecycleOptions.TerminalGracePeriod"/> has passed. Paused runs are never stopped.
/// </summary>
public class ExecutorLifecycleTests : TestKit
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void CompletedRun_WithZeroGrace_StopsExecutor()
    {
        var (executor, parent, _) = this.Start(Workflow(Node("n1", "lc.pass")), TimeSpan.Zero);
        this.Watch(executor);

        parent.FishForMessage(m => m is WorkflowCompleted, RunTimeout);

        this.ExpectTerminated(executor, TimeSpan.FromSeconds(5))
            .Should().NotBeNull(because: "a finished executor should stop so runs don't leak actors/memory (§12.2)");
    }

    [Fact]
    public void FailedRun_WithZeroGrace_StopsExecutor()
    {
        var (executor, parent, _) = this.Start(Workflow(Node("n1", "lc.not.registered")), TimeSpan.Zero);
        this.Watch(executor);

        parent.FishForMessage(m => m is WorkflowFailed, RunTimeout);

        this.ExpectTerminated(executor, TimeSpan.FromSeconds(5)).Should().NotBeNull();
    }

    [Fact]
    public void CompletedRun_AnswersLiveStatusDuringGrace_ThenStops()
    {
        var (executor, parent, executionId) = this.Start(Workflow(Node("n1", "lc.pass")), TimeSpan.FromSeconds(1));
        this.Watch(executor);

        parent.FishForMessage(m => m is WorkflowCompleted, RunTimeout);

        // Within the grace period the live actor still answers with full status.
        executor.Tell(new GetWorkflowStatus(executionId));
        var status = this.ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(3));
        status.State.Should().Be(ExecutionState.Completed);
        status.NodeStates.Find("n1").IfNone(NodeExecutionState.Pending).Should().Be(NodeExecutionState.Completed);

        this.ExpectTerminated(executor, TimeSpan.FromSeconds(5))
            .Should().NotBeNull(because: "the executor stops once the grace period has passed");
    }

    [Fact]
    public void PausedRun_IsNotStopped()
    {
        var definition = Workflow(
            new[] { Node("slow", "lc.slow"), Node("next", "lc.pass") },
            new[] { new ConnectionDefinition("slow", "output", "next", "input") });
        var (executor, _, executionId) = this.Start(definition, TimeSpan.Zero);
        this.Watch(executor);

        Thread.Sleep(100);
        executor.Tell(new PauseExecution(executionId));

        // Long enough for the slow node to finish and any (wrong) stop to happen.
        this.ExpectNoMsg(TimeSpan.FromSeconds(1.5));

        executor.Tell(new GetWorkflowStatus(executionId));
        var status = this.ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(3));
        status.State.Should().Be(ExecutionState.Paused, because: "a paused run is resumable and must not be reaped (§12.2)");
    }

    private (IActorRef Executor, Akka.TestKit.TestProbe Parent, Guid ExecutionId) Start(WorkflowDefinition definition, TimeSpan grace)
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new StubModule("lc.pass"));
        registry.RegisterModule(new SlowModule());

        var services = new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .AddSingleton(new ExecutorLifecycleOptions { TerminalGracePeriod = grace })
            .BuildServiceProvider();

        var executionId = Guid.NewGuid();
        var parent = this.CreateTestProbe();
        var executor = parent.ChildActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), services));
        executor.Tell(new StartExecution(executionId));
        return (executor, parent, executionId);
    }

    private static NodeDefinition Node(string id, string moduleId)
        => new(id, moduleId, id, HashMap<string, System.Text.Json.JsonElement>.Empty);

    private static WorkflowDefinition Workflow(NodeDefinition node)
        => Workflow(new[] { node }, Array.Empty<ConnectionDefinition>());

    private static WorkflowDefinition Workflow(NodeDefinition[] nodes, ConnectionDefinition[] connections)
        => new(
            Id: Guid.NewGuid(),
            Name: "executor-lifecycle-test",
            Description: null,
            Version: new Version(1, 0),
            Nodes: nodes.ToArr(),
            Connections: connections.ToArr(),
            Variables: HashMap<string, VariableDefinition>.Empty);

    /// <summary>Completes after 500ms, so a pause can land while it runs.</summary>
    private sealed class SlowModule : IWorkflowModule
    {
        public string ModuleId => "lc.slow";

        public string DisplayName => this.ModuleId;

        public string Category => "Test";

        public string Description => "Slow test module";

        public string Icon => "🐢";

        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Inputs: Arr<PortDefinition>.Empty,
            Outputs: Arr.create(PortDefinition.Create<object>("output", isRequired: false)),
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public async Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
        {
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            return ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = "done" });
        }
    }
}
