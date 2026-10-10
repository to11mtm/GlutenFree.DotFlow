// <copyright file="EventStreamPublishTests.cs" company="GlutenFree">
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
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Xunit;

/// <summary>
/// The executor publishes terminal and node-completion messages to the <c>EventStream</c> (§12.1),
/// as SLIM copies: outputs, partial outputs and variable updates are stripped so no
/// possibly-secret values go on the stream. The parent still receives the full messages.
/// </summary>
public class EventStreamPublishTests : TestKit
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void CompletedRun_PublishesSlimNodeAndWorkflowCompletion()
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new SecretOutputModule());
        var definition = SingleNode("es.secret");

        var stream = this.Subscribe(typeof(WorkflowCompleted), typeof(NodeExecutionCompleted));
        var parent = this.Run(definition, registry);

        var toParent = parent.FishForMessage<WorkflowCompleted>(_ => true, Timeout);
        toParent.Outputs.Should().NotBeEmpty(because: "the parent still gets the real outputs");

        var node = stream.FishForMessage<NodeExecutionCompleted>(_ => true, Timeout);
        node.NodeId.Should().Be("n1");
        node.Outputs.Should().BeEmpty(because: "node outputs must not be broadcast on the EventStream");
        node.VariableUpdates.Should().BeNull();

        var workflow = stream.FishForMessage<WorkflowCompleted>(_ => true, Timeout);
        workflow.ExecutionId.Should().Be(toParent.ExecutionId);
        workflow.Outputs.Should().BeEmpty(because: "workflow outputs must not be broadcast on the EventStream");
    }

    [Fact]
    public void FailedRun_PublishesNodeAndWorkflowFailure_WithoutPartialOutputs()
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        var definition = SingleNode("es.not.registered");

        var stream = this.Subscribe(typeof(WorkflowFailed), typeof(NodeExecutionFailed));
        this.Run(definition, registry);

        var node = stream.FishForMessage<NodeExecutionFailed>(_ => true, Timeout);
        node.NodeId.Should().Be("n1");
        node.Error.Message.Should().Contain("es.not.registered");

        var workflow = stream.FishForMessage<WorkflowFailed>(_ => true, Timeout);
        workflow.PartialOutputs.IsNone.Should().BeTrue();
    }

    private Akka.TestKit.TestProbe Subscribe(params Type[] types)
    {
        var probe = this.CreateTestProbe();
        foreach (var type in types)
        {
            this.Sys.EventStream.Subscribe(probe.Ref, type);
        }

        return probe;
    }

    private Akka.TestKit.TestProbe Run(WorkflowDefinition definition, IModuleRegistry registry)
    {
        var services = new ServiceCollection()
            .AddSingleton(registry)
            .BuildServiceProvider();

        var executionId = Guid.NewGuid();
        var parent = this.CreateTestProbe();
        var executor = parent.ChildActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), services));
        executor.Tell(new StartExecution(executionId));
        return parent;
    }

    private static WorkflowDefinition SingleNode(string moduleId)
        => new(
            Id: Guid.NewGuid(),
            Name: "event-stream-test",
            Description: null,
            Version: new Version(1, 0),
            Nodes: Arr.create(new NodeDefinition("n1", moduleId, "N1", HashMap<string, System.Text.Json.JsonElement>.Empty)),
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: HashMap<string, VariableDefinition>.Empty);

    /// <summary>Returns an output and a variable update that must not reach the EventStream.</summary>
    private sealed class SecretOutputModule : IWorkflowModule
    {
        public string ModuleId => "es.secret";

        public string DisplayName => this.ModuleId;

        public string Category => "Test";

        public string Description => "Emits a secret-looking output";

        public string Icon => "🔑";

        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Inputs: Arr<PortDefinition>.Empty,
            Outputs: Arr.create(PortDefinition.Create<object>("output", isRequired: false)),
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = "s3cr3t" }));
    }
}
