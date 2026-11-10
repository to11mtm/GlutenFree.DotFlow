// <copyright file="BodyNodeEventsTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Flow;
using Xunit;

/// <summary>
/// Follow-up — nodes inside construct bodies report started / completed / failed / retrying on the
/// EventStream like top-level nodes, so real-time clients see them (the bridge turns them into
/// NodeStarted / NodeCompleted / NodeFailed). Outputs are stripped, as at the top level.
/// </summary>
public sealed class BodyNodeEventsTests : TestKit
{
    [Fact]
    public void LoopBodyNodes_ReportStartedAndCompleted_InEveryIteration()
    {
        var events = this.Listen(typeof(NodeStateChanged), typeof(NodeExecutionCompleted));
        var definition = Workflow(
            [Foreach("loop", 3), Node("leaf", "e.pass")],
            [new ConnectionDefinition("loop", "loopBody", "leaf", "input")]);

        this.Run(definition, new ForEachModule(), new Stub("e.pass", fail: false)).Should().BeOfType<WorkflowCompleted>();

        var received = Drain(events);
        received.OfType<NodeStateChanged>().Count(e => e.NodeId == "leaf" && e.NewState == NodeExecutionState.Running).Should().Be(3);
        var completed = received.OfType<NodeExecutionCompleted>().Where(e => e.NodeId == "leaf").ToList();
        completed.Should().HaveCount(3);
        completed.Should().OnlyContain(e => e.Outputs.IsEmpty, because: "outputs are stripped from the published event, as at the top level");
    }

    [Fact]
    public void AFailingBodyNode_ReportsRetryingAndFailed()
    {
        var events = this.Listen(typeof(NodeRetrying), typeof(NodeExecutionFailed));
        var leaf = Node("leaf", "e.fail") with
        {
            ErrorHandling = new ErrorHandling(ErrorBehavior.Retry),
            RetryPolicy = new RetryPolicy(MaxAttempts: 2, DelayMs: 10, BackoffMultiplier: 1.0, MaxDelayMs: 50),
        };
        var definition = Workflow([Foreach("loop", 1), leaf], [new ConnectionDefinition("loop", "loopBody", "leaf", "input")]);

        this.Run(definition, new ForEachModule(), new Stub("e.fail", fail: true)).Should().BeOfType<WorkflowFailed>();

        var received = Drain(events);
        received.OfType<NodeRetrying>().Should().Contain(e => e.NodeId == "leaf" && e.Attempt == 1);
        received.OfType<NodeExecutionFailed>().Where(e => e.NodeId == "leaf").Should().HaveCount(2, because: "the first attempt and the retry both failed");
    }

    private TestProbe Listen(params Type[] types)
    {
        var probe = this.CreateTestProbe();
        foreach (var type in types)
        {
            Sys.EventStream.Subscribe(probe, type);
        }

        return probe;
    }

    private static List<object> Drain(TestProbe probe)
        => probe.ReceiveWhile<object>(TimeSpan.FromMilliseconds(500), m => m).ToList();

    private object Run(WorkflowDefinition definition, params IWorkflowModule[] modules)
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        foreach (var module in modules)
        {
            registry.RegisterModule(module);
        }

        var executionId = Guid.NewGuid();
        var parent = this.CreateTestProbe();
        var executor = parent.ChildActorOf(WorkflowExecutor.Props(
            executionId,
            definition,
            new Dictionary<string, object?>(),
            new ServiceCollection().AddSingleton<IModuleRegistry>(registry).BuildServiceProvider()));
        executor.Tell(new StartExecution(executionId));
        return parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(15));
    }

    private static NodeDefinition Foreach(string id, int count)
        => new(id, "builtin.loop.foreach", id, HashMap<string, JsonElement>.Empty.Add("collection", JsonSerializer.SerializeToElement(Enumerable.Range(1, count).ToArray())));

    private static NodeDefinition Node(string id, string moduleId) => new(id, moduleId, id, HashMap<string, JsonElement>.Empty);

    private static WorkflowDefinition Workflow(NodeDefinition[] nodes, ConnectionDefinition[] connections)
        => new(Guid.NewGuid(), "body-events", null, new Version(1, 0), nodes.ToArr(), connections.ToArr(), HashMap<string, VariableDefinition>.Empty);

    private sealed class Stub : IWorkflowModule
    {
        private readonly bool fail;

        public Stub(string moduleId, bool fail)
        {
            this.ModuleId = moduleId;
            this.fail = fail;
        }

        public string ModuleId { get; }

        public string DisplayName => this.ModuleId;

        public string Category => "Test";

        public string Description => "test";

        public string Icon => "🧪";

        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Arr.create(PortDefinition.Create<object>("input", isRequired: false)),
            Arr.create(PortDefinition.Create<object>("output", isRequired: false)),
            Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(this.fail
                ? ModuleResult.Fail("boom", new InvalidOperationException("boom"))
                : ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = "secret-ish output" }));
    }
}
