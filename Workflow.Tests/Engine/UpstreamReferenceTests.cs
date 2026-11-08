// <copyright file="UpstreamReferenceTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

using System;
using System.Collections.Generic;
using System.Linq;
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
/// §10.1 — <c>{{nodeId.port}}</c> resolves for any <b>ancestor</b> that has run (any node with a
/// connection path into this one), including from inside construct bodies to nodes that ran before
/// the construct. Non-ancestors, and ancestors that didn't run, fail with a message saying which.
/// </summary>
public sealed class UpstreamReferenceTests : TestKit
{
    [Fact]
    public void NonAdjacentAncestor_Resolves()
    {
        var sink = new CaptureModule("ur.sink", templatedProperties: new[] { "p" });
        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("src", "ur.src"),
                FindingDefs.Node("mid", "ur.pass"),
                FindingDefs.Node("last", "ur.pass"),
                FindingDefs.Node("sink", "ur.sink", properties: new (string, object?)[] { ("p", "{{src.value}}|{{mid.output}}|{{last.output}}") }),
            },
            connections: new[]
            {
                FindingDefs.Edge("src", "value", "mid", "input"),
                FindingDefs.Edge("mid", "output", "last", "input"),
                FindingDefs.Edge("last", "output", "sink", "input"),
            });

        var terminal = this.Run(definition, Src(), new PassModule("ur.pass"), sink);

        terminal.Should().BeOfType<WorkflowCompleted>(because: "{0}", (terminal as WorkflowFailed)?.Error.Message);
        sink.Captured.Single().Properties["p"].Should().Be("from-src|mid-done|last-done");
        sink.Captured.Single().Inputs.Keys.Should().NotContain("src.value", because: "ancestors are bindable, not merged into the node's inputs (history stays the same size)");
    }

    [Fact]
    public void ForEachBody_ReadsANodeThatRanBeforeTheLoop()
    {
        var deep = new CaptureModule("ur.deep", templatedProperties: new[] { "p" });
        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("src", "ur.src"),
                FindingDefs.Node("loop", "builtin.loop.foreach", properties: new (string, object?)[] { ("collection", new[] { "a", "b" }) }),
                FindingDefs.Node("first", "ur.pass"),
                FindingDefs.Node("deep", "ur.deep", properties: new (string, object?)[] { ("p", "{{src.value}}:{{Variable.item}}") }),
            },
            connections: new[]
            {
                FindingDefs.Edge("src", "value", "loop", "input"),
                FindingDefs.Edge("loop", "loopBody", "first", "input"),
                FindingDefs.Edge("first", "output", "deep", "input"),
            });

        var terminal = this.Run(definition, Src(), new ForEachModule(), new PassModule("ur.pass"), deep);

        terminal.Should().BeOfType<WorkflowCompleted>(because: "{0}", (terminal as WorkflowFailed)?.Error.Message);
        deep.Captured.Select(c => c.Properties["p"]).Should().Equal("from-src:a", "from-src:b");
    }

    [Fact]
    public void NestedBody_ReadsANodeThatRanBeforeTheOuterConstruct()
    {
        // src → try/catch · try → for each · loopBody → deep, and deep reads src two constructs out.
        var deep = new CaptureModule("ur.deep", templatedProperties: new[] { "p" });
        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("src", "ur.src"),
                FindingDefs.Node("tc", "builtin.trycatch"),
                FindingDefs.Node("loop", "builtin.loop.foreach", properties: new (string, object?)[] { ("collection", new[] { "a" }) }),
                FindingDefs.Node("deep", "ur.deep", properties: new (string, object?)[] { ("p", "{{src.value}}") }),
            },
            connections: new[]
            {
                FindingDefs.Edge("src", "value", "tc", "input"),
                FindingDefs.Edge("tc", "try", "loop", "input"),
                FindingDefs.Edge("loop", "loopBody", "deep", "input"),
            });

        var terminal = this.Run(definition, Src(), new TryCatchModule(), new ForEachModule(), deep);

        terminal.Should().BeOfType<WorkflowCompleted>(because: "{0}", (terminal as WorkflowFailed)?.Error.Message);
        deep.Captured.Select(c => c.Properties["p"]).Should().Equal("from-src");
    }

    [Fact]
    public void SiblingBranch_IsNotAnAncestor_AndFailsSayingSo()
    {
        var reader = new CaptureModule("ur.reader", templatedProperties: new[] { "p" });
        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("src", "ur.src"),
                FindingDefs.Node("left", "ur.pass"),
                FindingDefs.Node("right", "ur.reader", properties: new (string, object?)[] { ("p", "{{left.output}}") }),
            },
            connections: new[]
            {
                FindingDefs.Edge("src", "value", "left", "input"),
                FindingDefs.Edge("src", "value", "right", "input"),
            });

        var terminal = this.Run(definition, Src(), new PassModule("ur.pass"), reader);

        terminal.Should().BeOfType<WorkflowFailed>()
            .Which.Error.Message.Should().Contain("node 'left' isn't upstream of this node");
        reader.Captured.Should().BeEmpty();
    }

    [Fact]
    public void AncestorThatDidntRun_FailsSayingSo()
    {
        // Driven directly: 'skipped' is an ancestor with no outputs in the upstream view.
        var sink = new CaptureModule("ur.sink", templatedProperties: new[] { "p" });
        var services = Services(sink);
        var node = FindingDefs.Node("sink", "ur.sink", properties: new (string, object?)[] { ("p", "{{skipped.output}}") });
        var executionId = Guid.NewGuid();
        var executor = this.ActorOfAsTestActorRef<NodeExecutor>(
            NodeExecutor.Props("sink", node, new Dictionary<string, object?>(), executionId, services),
            this.TestActor);

        executor.Tell(new Execute(
            "sink",
            HashMap<string, object?>.Empty,
            executionId,
            Upstream: new UpstreamView(new Dictionary<string, IReadOnlyDictionary<string, object?>>(), new[] { "skipped" })));

        this.ExpectMsg<NodeExecutionFailed>(TimeSpan.FromSeconds(5))
            .Error.Message.Should().Contain("node 'skipped' is upstream of this node but hasn't produced output in this run");
    }

    private static OutputModule Src() => new("ur.src", new Dictionary<string, object?> { ["value"] = "from-src" });

    private static IServiceProvider Services(params IWorkflowModule[] modules)
        => FindingDefs.Services(modules, s =>
        {
            s.AddSingleton<IExpressionEvaluator>(new JintExpressionEvaluator(NullLogger<JintExpressionEvaluator>.Instance));
            s.AddSingleton<IPropertyBinder>(sp => new PropertyBinder(sp.GetService<IExpressionEvaluator>()));
        });

    private object Run(WorkflowDefinition definition, params IWorkflowModule[] modules)
    {
        var id = Guid.NewGuid();
        var parent = this.CreateTestProbe();
        var executor = parent.ChildActorOf(WorkflowExecutor.Props(id, definition, new Dictionary<string, object?>(), Services(modules)));
        executor.Tell(new StartExecution(id));
        return parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, FindingDefs.RunTimeout);
    }
}
