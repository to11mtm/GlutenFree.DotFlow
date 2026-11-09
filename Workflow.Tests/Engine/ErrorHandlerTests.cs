// <copyright file="ErrorHandlerTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using LanguageExt;
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
/// §12.7 — <see cref="ErrorBehavior.UseErrorHandler"/>: a failing node (after its retries) runs its
/// <c>ErrorNodeId</c> handler with <c>{{Variable.error}}</c>; its downstream is skipped and the
/// handler's own path continues; handler nodes never start on their own; a failing handler fails
/// the run; <c>MaxConsecutiveErrors</c> bounds repeated handled failures. See docs/error-handling.md.
/// </summary>
public sealed class ErrorHandlerTests : TestKit
{
    private static ErrorHandling UseHandler(string handlerId, int? max = null) => new(ErrorBehavior.UseErrorHandler, handlerId, max);

    [Fact]
    public void Failure_RunsTheHandlerPath_SkipsTheFailedNodesDownstream_AndTheRunCompletes()
    {
        var handler = new CaptureModule("eh.handler", templatedProperties: new[] { "p" });
        var notify = new PassModule("eh.notify");
        var after = new PassModule("eh.after");
        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("boom", "eh.boom", errorHandling: UseHandler("handler")),
                FindingDefs.Node("after", "eh.after"),
                FindingDefs.Node("handler", "eh.handler", properties: new (string, object?)[] { ("p", "{{Variable.error.message}}|{{Variable.error.nodeId}}") }),
                FindingDefs.Node("notify", "eh.notify"),
            },
            connections: new[]
            {
                FindingDefs.Edge("boom", "output", "after", "input"),
                FindingDefs.Edge("handler", "output", "notify", "input"),
            });

        var terminal = this.Run(definition, new FlakyModule("eh.boom"), after, handler, notify);

        terminal.Should().BeOfType<WorkflowCompleted>(because: "{0}", (terminal as WorkflowFailed)?.Error.Message);
        handler.Captured.Single().Properties["p"].Should().Be("intentional failure|boom");
        notify.Calls.Should().Be(1, because: "the handler's own path continues");
        after.Calls.Should().Be(0, because: "the failed node's downstream is skipped");
    }

    [Fact]
    public void WithoutAFailure_TheHandlerNeverRuns()
    {
        var handler = new PassModule("eh.handler");
        var notify = new PassModule("eh.notify");
        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("ok", "eh.ok", errorHandling: UseHandler("handler")),
                FindingDefs.Node("handler", "eh.handler"),
                FindingDefs.Node("notify", "eh.notify"),
            },
            connections: new[] { FindingDefs.Edge("handler", "output", "notify", "input") });

        var terminal = this.Run(definition, new PassModule("eh.ok"), handler, notify);

        terminal.Should().BeOfType<WorkflowCompleted>();
        handler.Calls.Should().Be(0, because: "a handler node doesn't start with the run on its own");
        notify.Calls.Should().Be(0);
    }

    [Fact]
    public void RetriesComeFirst_TheHandlerRunsOnlyOnceTheyAreUsedUp()
    {
        var recovering = new FlakyModule("eh.recovers", failTimes: 1);
        var recoveringHandler = new PassModule("eh.handler1");
        var recovered = this.Run(
            FindingDefs.Definition(nodes: new[]
            {
                FindingDefs.Node("n", "eh.recovers", errorHandling: UseHandler("h"), retry: QuickRetry(3)),
                FindingDefs.Node("h", "eh.handler1"),
            }),
            recovering,
            recoveringHandler);

        recovered.Should().BeOfType<WorkflowCompleted>();
        recovering.Calls.Should().Be(2);
        recoveringHandler.Calls.Should().Be(0, because: "the retry succeeded");

        var failing = new FlakyModule("eh.always");
        var handler = new PassModule("eh.handler2");
        var handled = this.Run(
            FindingDefs.Definition(nodes: new[]
            {
                FindingDefs.Node("n", "eh.always", errorHandling: UseHandler("h"), retry: QuickRetry(2)),
                FindingDefs.Node("h", "eh.handler2"),
            }),
            failing,
            handler);

        handled.Should().BeOfType<WorkflowCompleted>();
        failing.Calls.Should().Be(2, because: "the policy allows 2 attempts");
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public void AFailingHandler_FailsTheRun_NamingBothErrors()
    {
        var terminal = this.Run(
            FindingDefs.Definition(nodes: new[]
            {
                FindingDefs.Node("n", "eh.boom", errorHandling: UseHandler("h")),
                FindingDefs.Node("h", "eh.handlerboom"),
            }),
            new FlakyModule("eh.boom"),
            new FlakyModule("eh.handlerboom"));

        terminal.Should().BeOfType<WorkflowFailed>()
            .Which.Error.Message.Should().Contain("The error handler for node 'n' failed");
    }

    [Fact]
    public void AWorkflowLevelHandler_IsTheDefault()
    {
        var handler = new PassModule("eh.handler");
        var definition = FindingDefs.Definition(nodes: new[]
        {
            FindingDefs.Node("n", "eh.boom"),
            FindingDefs.Node("h", "eh.handler"),
        }) with { ErrorHandling = UseHandler("h") };

        var terminal = this.Run(definition, new FlakyModule("eh.boom"), handler);

        terminal.Should().BeOfType<WorkflowCompleted>();
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public void AHandlerWithItsOwnInputs_StillRunsNormally()
    {
        var handler = new PassModule("eh.handler");
        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("start", "eh.ok"),
                FindingDefs.Node("ok", "eh.ok", errorHandling: UseHandler("handler")),
                FindingDefs.Node("handler", "eh.handler"),
            },
            connections: new[] { FindingDefs.Edge("start", "output", "handler", "input") });

        var terminal = this.Run(definition, new PassModule("eh.ok"), handler);

        terminal.Should().BeOfType<WorkflowCompleted>();
        handler.Calls.Should().Be(1, because: "it's wired in from 'start'");
    }

    [Fact]
    public void InALoop_MaxConsecutiveErrors_FailsTheRunOnceExceeded()
    {
        var handler = new PassModule("eh.handler");
        var definition = FindingDefs.Definition(nodes: new[]
            {
                FindingDefs.Node("loop", "builtin.loop.foreach", properties: new (string, object?)[] { ("collection", new[] { "a", "b", "c", "d" }) }),
                FindingDefs.Node("work", "eh.boom", errorHandling: UseHandler("h", max: 2)),
                FindingDefs.Node("h", "eh.handler"),
            },
            connections: new[] { FindingDefs.Edge("loop", "loopBody", "work", "input") });

        var terminal = this.Run(definition, new ForEachModule(), new FlakyModule("eh.boom"), handler);

        terminal.Should().BeOfType<WorkflowFailed>()
            .Which.Error.Message.Should().Contain("failed 3 times in a row, more than its MaxConsecutiveErrors (2)");
        handler.Calls.Should().Be(2, because: "the first two failures were handled");
    }

    [Fact]
    public void InALoop_ASuccessResetsTheCount()
    {
        var handler = new PassModule("eh.handler");
        var definition = FindingDefs.Definition(nodes: new[]
            {
                FindingDefs.Node("loop", "builtin.loop.foreach", properties: new (string, object?)[] { ("collection", new[] { "bad", "ok", "bad", "ok", "bad" }) }),
                FindingDefs.Node("work", "eh.picky", errorHandling: UseHandler("h", max: 1)),
                FindingDefs.Node("h", "eh.handler"),
            },
            connections: new[] { FindingDefs.Edge("loop", "loopBody", "work", "input") });

        var terminal = this.Run(definition, new ForEachModule(), new FailsOnBadItem("eh.picky"), handler);

        terminal.Should().BeOfType<WorkflowCompleted>(because: "never more than 1 failure in a row ({0})", (terminal as WorkflowFailed)?.Error.Message);
        handler.Calls.Should().Be(3);
    }

    private static RetryPolicy QuickRetry(int maxAttempts) => new(MaxAttempts: maxAttempts, DelayMs: 10, BackoffMultiplier: 1.0, MaxDelayMs: 20);

    private object Run(WorkflowDefinition definition, params IWorkflowModule[] modules)
    {
        var services = FindingDefs.Services(modules, s =>
        {
            s.AddSingleton<IExpressionEvaluator>(new JintExpressionEvaluator(NullLogger<JintExpressionEvaluator>.Instance));
            s.AddSingleton<IPropertyBinder>(sp => new PropertyBinder(sp.GetService<IExpressionEvaluator>()));
        });
        var id = Guid.NewGuid();
        var parent = this.CreateTestProbe();
        var executor = parent.ChildActorOf(WorkflowExecutor.Props(id, definition, new Dictionary<string, object?>(), services));
        executor.Tell(new StartExecution(id));
        return parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, FindingDefs.RunTimeout);
    }

    /// <summary>Fails when the loop's current item is "bad".</summary>
    private sealed class FailsOnBadItem : IWorkflowModule
    {
        public FailsOnBadItem(string id) => this.ModuleId = id;

        public string ModuleId { get; }

        public string DisplayName => this.ModuleId;

        public string Category => "Test";

        public string Description => "fails on item 'bad'";

        public string Icon => "🧪";

        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Arr.create(PortDefinition.Create<object>("input", isRequired: false)),
            Arr.create(PortDefinition.Create<object>("output", isRequired: false)),
            Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(context.Variables.TryGetValue("item", out var item) && Equals(item?.ToString(), "bad")
                ? ModuleResult.Fail("bad item", new InvalidOperationException("bad item"))
                : ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = item }));
    }
}
