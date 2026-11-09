// <copyright file="EdgeConditionsOffActorThreadTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Core.Abstractions;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Engine.Services;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Binding;
using Workflow.Modules.Builtin.Flow;
using Workflow.Tests.Findings;
using Xunit;

/// <summary>
/// Async binding (follow-up to Stages 7.2 and 7.9) — a completed node's edge conditions are evaluated
/// off the <see cref="WorkflowExecutor"/> / <see cref="SubGraphExecutor"/> thread and the result is
/// piped back, so a slow condition doesn't stop the executor answering a status request, taking a
/// cancel or moving other branches on. The slow binder below blocks (it doesn't await), the way a
/// synchronous evaluator (DynamicExpresso) or a custom binder can, so a missing <c>Task.Run</c> shows.
/// </summary>
public sealed class EdgeConditionsOffActorThreadTests : TestKit
{
    private static readonly TimeSpan ConditionTime = TimeSpan.FromMilliseconds(1500);

    private static ConnectionDefinition Edge(string from, string port, string to, string? condition = null)
        => new(from, port, to, "input", condition, 0);

    private static WorkflowDefinition SourceThenTarget() => FindingDefs.Definition(
        nodes: new[]
        {
            FindingDefs.Node("src", "ec.src"),
            FindingDefs.Node("next", "ec.next"),
        },
        connections: new[] { Edge("src", "output", "next", "value != null") });

    [Fact]
    public void TheExecutor_AnswersAStatusRequest_WhileAConditionEvaluates()
    {
        var binder = new SlowConditionBinder(ConditionTime);
        var next = new PassModule("ec.next");
        var (executor, parent, executionId) = this.Start(SourceThenTarget(), binder, new PassModule("ec.src"), next);

        binder.Started.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue(because: "the source node completes and its condition starts");
        var asked = Stopwatch.StartNew();
        executor.Tell(new GetWorkflowStatus(executionId));
        var status = this.ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(5));
        asked.Stop();

        asked.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(700), because: "the executor isn't blocked by the condition");
        status.NodeStates["src"].Should().Be(NodeExecutionState.Running, because: "the node completes once its edges are decided");
        binder.Finished.IsSet.Should().BeFalse(because: "the reply came while the condition was still running");

        parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, FindingDefs.RunTimeout)
            .Should().BeOfType<WorkflowCompleted>();
        next.Calls.Should().Be(1, because: "the condition was true once it finished");
    }

    [Fact]
    public void ACancel_IsHandledAtOnce_WhileAConditionEvaluates_AndTheLateResultIsIgnored()
    {
        var binder = new SlowConditionBinder(ConditionTime);
        var next = new PassModule("ec.next");
        var (executor, parent, executionId) = this.Start(SourceThenTarget(), binder, new PassModule("ec.src"), next);

        binder.Started.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
        var asked = Stopwatch.StartNew();
        executor.Tell(new CancelExecution(executionId));

        var terminal = parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(5));
        asked.Stop();
        terminal.Should().BeOfType<WorkflowFailed>().Which.Error.Should().BeAssignableTo<OperationCanceledException>();
        asked.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(700), because: "the cancel wasn't queued behind the condition");
        binder.Finished.IsSet.Should().BeFalse();

        // The condition finishes ~1.5 s in; its result must not route anything or end the run again.
        parent.ExpectNoMsg(ConditionTime + TimeSpan.FromSeconds(1));
        binder.Finished.IsSet.Should().BeTrue(because: "the window above covers the late result");
        next.Calls.Should().Be(0);
        executor.Tell(new GetWorkflowStatus(executionId));
        this.ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(3)).State.Should().Be(ExecutionState.Cancelled);
    }

    /// <summary>
    /// In a loop body: while <c>pick</c>'s condition runs, the parallel branch <c>fast → after</c>
    /// still moves on, so <c>after</c> runs before the condition is done.
    /// </summary>
    [Fact]
    public void InsideALoopBody_OtherBranchesMoveOn_WhileAConditionEvaluates()
    {
        var binder = new SlowConditionBinder(ConditionTime);
        var after = new TimedPass("ec.after");
        var next = new PassModule("ec.next");
        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("loop", "builtin.loop.foreach", properties: new (string, object?)[] { ("collection", new[] { 1 }) }),
                FindingDefs.Node("pick", "ec.pick"),
                FindingDefs.Node("next", "ec.next"),
                FindingDefs.Node("fast", "ec.fast"),
                FindingDefs.Node("after", "ec.after"),
            },
            connections: new[]
            {
                Edge("loop", "loopBody", "pick"),
                Edge("pick", "output", "next", "value != null"),
                Edge("loop", "loopBody", "fast"),
                Edge("fast", "output", "after"),
            });

        var (_, parent, _) = this.Start(definition, binder, new ForEachModule(), new PassModule("ec.pick"), next, new PassModule("ec.fast", delayMs: 300), after);

        var terminal = parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, FindingDefs.RunTimeout);
        terminal.Should().BeOfType<WorkflowCompleted>(because: "{0}", (terminal as WorkflowFailed)?.Error.Message);
        next.Calls.Should().Be(1);
        after.RanAt.Should().BeBefore(binder.FinishedAt, because: "the body's executor kept going while the condition ran");
    }

    private (IActorRef Executor, Akka.TestKit.TestProbe Parent, Guid ExecutionId) Start(WorkflowDefinition definition, IPropertyBinder binder, params IWorkflowModule[] modules)
    {
        var services = FindingDefs.Services(modules, s =>
        {
            s.AddSingleton<IExpressionEvaluator>(new JintExpressionEvaluator(NullLogger<JintExpressionEvaluator>.Instance));
            s.AddSingleton(binder);
        });
        var id = Guid.NewGuid();
        var parent = this.CreateTestProbe();
        var executor = parent.ChildActorOf(WorkflowExecutor.Props(id, definition, new Dictionary<string, object?>(), services));
        executor.Tell(new StartExecution(id));
        return (executor, parent, id);
    }

    /// <summary>A real binder whose conditions take a while and block the calling thread.</summary>
    private sealed class SlowConditionBinder : IPropertyBinder
    {
        private readonly TimeSpan delay;
        private readonly PropertyBinder inner = new(new JintExpressionEvaluator(NullLogger<JintExpressionEvaluator>.Instance));

        public SlowConditionBinder(TimeSpan delay) => this.delay = delay;

        public ManualResetEventSlim Started { get; } = new();

        public ManualResetEventSlim Finished { get; } = new();

        public DateTimeOffset FinishedAt { get; private set; } = DateTimeOffset.MaxValue;

        public ValueTask<PropertyBindingResult> BindPropertiesAsync(IReadOnlyDictionary<string, object?> rawValues, LanguageExt.Arr<PortDefinition> schema, PropertyBindingContext context, CancellationToken cancellationToken = default)
            => this.inner.BindPropertiesAsync(rawValues, schema, context, cancellationToken);

        public ValueTask<PropertyBindingResult> BindModulePropertiesAsync(IReadOnlyDictionary<string, object?> rawValues, LanguageExt.Arr<ModulePropertyDefinition> schema, PropertyBindingContext context, CancellationToken cancellationToken = default)
            => this.inner.BindModulePropertiesAsync(rawValues, schema, context, cancellationToken);

        public ValueTask<ConditionResult> EvaluateConditionAsync(string expression, PropertyBindingContext context, object? value, CancellationToken cancellationToken = default)
        {
            this.Started.Set();
            Thread.Sleep(this.delay); // blocks, like a synchronous evaluator~
            this.FinishedAt = DateTimeOffset.UtcNow;
            this.Finished.Set();
            return this.inner.EvaluateConditionAsync(expression, context, value, CancellationToken.None);
        }
    }

    /// <summary>Records when it ran.</summary>
    private sealed class TimedPass : FindingModuleBase
    {
        public TimedPass(string moduleId)
            : base(moduleId)
        {
        }

        public DateTimeOffset RanAt { get; private set; } = DateTimeOffset.MaxValue;

        public override ModuleSchema Schema => new(Ports("input"), Ports("output"), LanguageExt.Arr<ModulePropertyDefinition>.Empty);

        protected override Task<ModuleResult> RunAsync(ModuleExecutionContext context, CancellationToken cancellationToken)
        {
            this.RanAt = DateTimeOffset.UtcNow;
            return Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = "after" }));
        }
    }
}
