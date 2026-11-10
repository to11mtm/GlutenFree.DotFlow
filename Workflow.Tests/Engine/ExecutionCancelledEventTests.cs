// <copyright file="ExecutionCancelledEventTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

using System;
using System.Collections.Generic;
using System.Text.Json;
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
/// Follow-up — a user cancel is marked as one (<see cref="WorkflowFailed.Cancelled"/>), both to the
/// parent and on the EventStream, so real-time clients get <c>ExecutionCancelled</c> rather than an
/// <c>ExecutionFailed</c> they'd have to recognise by its text. A real failure isn't marked.
/// </summary>
public sealed class ExecutionCancelledEventTests : TestKit
{
    [Fact]
    public void AUserCancel_IsMarkedCancelled_ToTheParentAndOnTheEventStream()
    {
        var executionId = Guid.NewGuid();
        Sys.EventStream.Subscribe(TestActor, typeof(WorkflowFailed));
        var parent = CreateTestProbe();
        var executor = parent.ChildActorOf(WorkflowExecutor.Props(executionId, Definition("test.slow"), new Dictionary<string, object?>(), Services(new Module("test.slow", TimeSpan.FromSeconds(30)))));

        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(200);
        executor.Tell(new CancelExecution(executionId));

        parent.FishForMessage<WorkflowFailed>(_ => true, TimeSpan.FromSeconds(10)).Cancelled.Should().BeTrue();
        FishForMessage<WorkflowFailed>(m => m.ExecutionId == executionId, TimeSpan.FromSeconds(10)).Cancelled.Should().BeTrue();
    }

    [Fact]
    public void ARealFailure_IsNotMarkedCancelled()
    {
        var executionId = Guid.NewGuid();
        var parent = CreateTestProbe();
        var executor = parent.ChildActorOf(WorkflowExecutor.Props(executionId, Definition("test.boom"), new Dictionary<string, object?>(), Services(new Module("test.boom", TimeSpan.Zero, fail: true))));

        executor.Tell(new StartExecution(executionId));

        parent.FishForMessage<WorkflowFailed>(_ => true, TimeSpan.FromSeconds(10)).Cancelled.Should().BeFalse();
    }

    private static WorkflowDefinition Definition(string moduleId)
        => new(
            Guid.NewGuid(),
            "cancel-test",
            null,
            new Version(1, 0),
            new Arr<NodeDefinition>(new[] { new NodeDefinition("n1", moduleId, "n1", HashMap<string, JsonElement>.Empty, Timeout: 60000) }),
            Arr<ConnectionDefinition>.Empty,
            HashMap<string, VariableDefinition>.Empty);

    private static IServiceProvider Services(IWorkflowModule module)
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(module);
        return new ServiceCollection().AddSingleton<IModuleRegistry>(registry).BuildServiceProvider();
    }

    private sealed class Module : IWorkflowModule
    {
        private readonly TimeSpan delay;
        private readonly bool fail;

        public Module(string moduleId, TimeSpan delay, bool fail = false)
        {
            this.ModuleId = moduleId;
            this.delay = delay;
            this.fail = fail;
        }

        public string ModuleId { get; }

        public string DisplayName => this.ModuleId;

        public string Category => "Test";

        public string Description => "test";

        public string Icon => "⏳";

        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(Arr<PortDefinition>.Empty, Arr.create(PortDefinition.Create<object>("output", isRequired: false)), Arr<ModulePropertyDefinition>.Empty);

        public async Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
        {
            await Task.Delay(this.delay, cancellationToken);
            return this.fail
                ? ModuleResult.Fail("boom", new InvalidOperationException("boom"))
                : ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = "ok" });
        }
    }
}
