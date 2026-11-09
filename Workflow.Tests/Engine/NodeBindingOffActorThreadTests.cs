// <copyright file="NodeBindingOffActorThreadTests.cs" company="GlutenFree">
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
using Workflow.Modules.Binding;
using Workflow.Modules.Builtin;
using Xunit;

/// <summary>
/// Stage 7.9 — binding a node's inputs and properties (which can evaluate expressions) runs off the
/// <see cref="NodeExecutor"/>'s actor thread, inside the node's execution: the node's timeout covers
/// it and the actor stays responsive (cancel) while it runs.
/// </summary>
public sealed class NodeBindingOffActorThreadTests : TestKit
{
    private static NodeDefinition PassThrough(int? timeoutMs = null)
        => new("n1", "builtin.passthrough", "n1", HashMap<string, JsonElement>.Empty, Timeout: timeoutMs);

    private static IServiceProvider Services(IPropertyBinder binder)
    {
        var registry = new InMemoryModuleRegistry();
        registry.RegisterModule(new PassThroughModule());
        return new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .AddSingleton(binder)
            .BuildServiceProvider();
    }

    private IActorRef Start(NodeDefinition node, IServiceProvider services, Akka.TestKit.TestProbe parent, Guid executionId)
    {
        var inputs = new Dictionary<string, object?> { ["input"] = "hello" };
        var executor = parent.ChildActorOf(NodeExecutor.Props(node.Id, node, inputs, executionId, services));
        executor.Tell(new Execute(node.Id, inputs.ToHashMap(), executionId));
        return executor;
    }

    [Fact]
    public void TheNodeTimeout_CoversSlowBinding()
    {
        var parent = CreateTestProbe();
        var executionId = Guid.NewGuid();

        Start(PassThrough(timeoutMs: 200), Services(new SlowBinder(TimeSpan.FromMilliseconds(1500))), parent, executionId);

        var failed = parent.ExpectMsg<NodeExecutionFailed>(TimeSpan.FromMilliseconds(1000));
        failed.Error.Should().BeOfType<TimeoutException>();
        parent.ExpectNoMsg(TimeSpan.FromMilliseconds(2000)); // the binding finishes ~1.5 s in and must report nothing more
    }

    /// <summary>
    /// A cancel sent while binding runs is handled at once (not queued behind binding), so the
    /// module never starts and the node reports cancelled. Cancel stays cooperative: the report comes
    /// when the binding in progress returns.
    /// </summary>
    [Fact]
    public void ACancelDuringBinding_StopsTheNodeBeforeTheModuleRuns()
    {
        var parent = CreateTestProbe();
        var executionId = Guid.NewGuid();

        var executor = Start(PassThrough(), Services(new SlowBinder(TimeSpan.FromMilliseconds(800))), parent, executionId);
        Thread.Sleep(100);
        executor.Tell(new CancelExecution(executionId));

        var report = parent.ExpectMsg<object>(TimeSpan.FromSeconds(5));
        report.Should().BeOfType<NodeExecutionFailed>(because: "the cancel reached the node before its module started")
            .Which.Error.Should().BeAssignableTo<OperationCanceledException>();
        parent.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void ABindingFailure_StillFailsTheNode_WithTheSameMessage()
    {
        var parent = CreateTestProbe();
        Start(PassThrough(), Services(new FailingBinder(inputs: true)), parent, Guid.NewGuid());
        parent.ExpectMsg<NodeExecutionFailed>(TimeSpan.FromSeconds(5))
            .Error.Message.Should().Be("Input binding failed: nope");

        var parent2 = CreateTestProbe();
        Start(PassThrough(), Services(new FailingBinder(inputs: false)), parent2, Guid.NewGuid());
        parent2.ExpectMsg<NodeExecutionFailed>(TimeSpan.FromSeconds(5))
            .Error.Message.Should().Be("Property binding failed: nope");
    }

    [Fact]
    public void Binding_StillFeedsTheModule()
    {
        var parent = CreateTestProbe();
        Start(PassThrough(), Services(new SlowBinder(TimeSpan.Zero)), parent, Guid.NewGuid());

        parent.ExpectMsg<NodeExecutionCompleted>(TimeSpan.FromSeconds(5))
            .Outputs["input"].Should().Be("hello");
    }

    /// <summary>
    /// A real binder that takes its time on the inputs (like a slow expression). It blocks rather
    /// than awaits, the way a synchronous evaluator (DynamicExpresso) or a custom binder can.
    /// </summary>
    private sealed class SlowBinder : IPropertyBinder
    {
        private readonly TimeSpan delay;
        private readonly PropertyBinder inner = new();

        public SlowBinder(TimeSpan delay) => this.delay = delay;

        public ValueTask<PropertyBindingResult> BindPropertiesAsync(IReadOnlyDictionary<string, object?> rawValues, Arr<PortDefinition> schema, PropertyBindingContext context, CancellationToken cancellationToken = default)
        {
            Thread.Sleep(this.delay);
            return this.inner.BindPropertiesAsync(rawValues, schema, context, cancellationToken);
        }

        public ValueTask<PropertyBindingResult> BindModulePropertiesAsync(IReadOnlyDictionary<string, object?> rawValues, Arr<ModulePropertyDefinition> schema, PropertyBindingContext context, CancellationToken cancellationToken = default)
            => this.inner.BindModulePropertiesAsync(rawValues, schema, context, cancellationToken);
    }

    private sealed class FailingBinder : IPropertyBinder
    {
        private readonly bool inputs;
        private readonly PropertyBinder inner = new();

        public FailingBinder(bool inputs) => this.inputs = inputs;

        public ValueTask<PropertyBindingResult> BindPropertiesAsync(IReadOnlyDictionary<string, object?> rawValues, Arr<PortDefinition> schema, PropertyBindingContext context, CancellationToken cancellationToken = default)
            => this.inputs ? ValueTask.FromResult(PropertyBindingResult.Fail("nope")) : this.inner.BindPropertiesAsync(rawValues, schema, context, cancellationToken);

        public ValueTask<PropertyBindingResult> BindModulePropertiesAsync(IReadOnlyDictionary<string, object?> rawValues, Arr<ModulePropertyDefinition> schema, PropertyBindingContext context, CancellationToken cancellationToken = default)
            => this.inputs ? this.inner.BindModulePropertiesAsync(rawValues, schema, context, cancellationToken) : ValueTask.FromResult(PropertyBindingResult.Fail("nope"));
    }
}
