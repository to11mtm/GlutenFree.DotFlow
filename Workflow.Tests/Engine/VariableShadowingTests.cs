// <copyright file="VariableShadowingTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

using System;
using System.Collections.Generic;
using System.Linq;
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
/// §10.2 — values arriving on a node's ports are not variables. <c>{{Variable.x}}</c> reads only real
/// variables, so an input port that shares a variable's name can't hide it. Construct bodies get
/// their per-iteration values (<c>item</c>, <c>index</c>, <c>error</c>) as body-scoped variables.
/// </summary>
public sealed class VariableShadowingTests : TestKit
{
    [Fact]
    public void InputPort_NamedLikeADeclaredVariable_DoesNotShadowIt()
    {
        var sink = new CaptureModule("vs.sink", inputPorts: new[] { "count" }, templatedProperties: new[] { "p" });
        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("src", "vs.src"),
                FindingDefs.Node("sink", "vs.sink", properties: new (string, object?)[] { ("p", "{{Variable.count}}") }),
            },
            connections: new[] { FindingDefs.Edge("src", "value", "sink", "count") },
            variables: new[] { new VariableDefinition("count", PropertyType.Int, FindingDefs.Json(99)) });

        var terminal = this.Run(definition, new OutputModule("vs.src", new Dictionary<string, object?> { ["value"] = 7 }), sink);

        terminal.Should().BeOfType<WorkflowCompleted>();
        sink.Captured.Single().Properties["p"].Should().Be(99, because: "{{Variable.count}} is the declared variable, not the value on the 'count' port");
    }

    [Fact]
    public void PortValue_ReadAsAVariable_FailsWithAHintAtTheRightSyntax()
    {
        var sink = new CaptureModule("vs.sink", inputPorts: new[] { "count" }, templatedProperties: new[] { "p" });
        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("src", "vs.src"),
                FindingDefs.Node("sink", "vs.sink", properties: new (string, object?)[] { ("p", "{{Variable.count}}") }),
            },
            connections: new[] { FindingDefs.Edge("src", "value", "sink", "count") });

        var terminal = this.Run(definition, new OutputModule("vs.src", new Dictionary<string, object?> { ["value"] = 7 }), sink);

        terminal.Should().BeOfType<WorkflowFailed>()
            .Which.Error.Message.Should().Contain("variable 'count' not found")
            .And.Contain("'count' is a value arriving on this node's input, not a variable");
        sink.Captured.Should().BeEmpty();
    }

    [Fact]
    public void RunInputs_AreStillVariables()
    {
        var sink = new CaptureModule("vs.sink", templatedProperties: new[] { "p" });
        var definition = FindingDefs.Definition(
            nodes: new[] { FindingDefs.Node("sink", "vs.sink", properties: new (string, object?)[] { ("p", "{{Variable.orderId}}") }) });

        var terminal = this.Run(definition, new Dictionary<string, object?> { ["orderId"] = "o-42" }, sink);

        terminal.Should().BeOfType<WorkflowCompleted>();
        sink.Captured.Single().Properties["p"].Should().Be("o-42");
    }

    [Fact]
    public void ForEachBody_ReadsItemAndIndexAsVariables_DeepInTheBody_WithoutLeakingThem()
    {
        var deep = new CaptureModule("vs.deep", templatedProperties: new[] { "p" });
        var after = new CaptureModule("vs.after", templatedProperties: new[] { "p" });
        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("loop", "builtin.loop.foreach", properties: new (string, object?)[] { ("collection", new[] { "a", "b" }) }),
                FindingDefs.Node("first", "vs.pass"),
                FindingDefs.Node("deep", "vs.deep", properties: new (string, object?)[] { ("p", "{{Variable.item}}-{{Variable.index}}") }),
                FindingDefs.Node("after", "vs.after", properties: new (string, object?)[] { ("p", "{{Variable.item}}") }),
            },
            connections: new[]
            {
                FindingDefs.Edge("loop", "loopBody", "first", "input"),
                FindingDefs.Edge("first", "output", "deep", "input"),
                FindingDefs.Edge("loop", "done", "after", "input"),
            },
            variables: new[] { new VariableDefinition("item", PropertyType.String, FindingDefs.Json("outer")) });

        var terminal = this.Run(definition, new ForEachModule(), new PassModule("vs.pass"), deep, after);

        terminal.Should().BeOfType<WorkflowCompleted>(because: "{0}", (terminal as WorkflowFailed)?.Error.Message);
        deep.Captured.Select(c => c.Properties["p"]).Should().Equal("a-0", "b-1");
        after.Captured.Single().Properties["p"].Should().Be("outer", because: "the body's item is body-scoped — it isn't a write");
    }

    [Fact]
    public void FanOutBranch_ReadsItemAsAVariable()
    {
        var branch = new CaptureModule("vs.branch", templatedProperties: new[] { "p" });
        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("fan", "builtin.fanout", properties: new (string, object?)[] { ("items", new[] { "x", "y" }) }),
                FindingDefs.Node("first", "vs.pass"),
                FindingDefs.Node("branch", "vs.branch", properties: new (string, object?)[] { ("p", "{{Variable.item}}@{{Variable.index}}") }),
            },
            connections: new[]
            {
                FindingDefs.Edge("fan", "branch", "first", "input"),
                FindingDefs.Edge("first", "output", "branch", "input"),
            });

        var terminal = this.Run(definition, new FanOutModule(), new PassModule("vs.pass"), branch);

        terminal.Should().BeOfType<WorkflowCompleted>(because: "{0}", (terminal as WorkflowFailed)?.Error.Message);
        branch.Captured.Select(c => c.Properties["p"]).Should().BeEquivalentTo(new[] { "x@0", "y@1" });
    }

    [Fact]
    public void CatchBody_ReadsTheErrorAsAVariable()
    {
        var handler = new CaptureModule("vs.handler", templatedProperties: new[] { "p" });
        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("tc", "builtin.trycatch"),
                FindingDefs.Node("boom", "vs.boom"),
                FindingDefs.Node("first", "vs.pass"),
                FindingDefs.Node("handler", "vs.handler", properties: new (string, object?)[] { ("p", "{{Variable.error.message}}") }),
            },
            connections: new[]
            {
                FindingDefs.Edge("tc", "try", "boom", "input"),
                FindingDefs.Edge("tc", "catch", "first", "input"),
                FindingDefs.Edge("first", "output", "handler", "input"),
            });

        var terminal = this.Run(definition, new TryCatchModule(), new FlakyModule("vs.boom"), new PassModule("vs.pass"), handler);

        terminal.Should().BeOfType<WorkflowCompleted>(because: "{0}", (terminal as WorkflowFailed)?.Error.Message);
        handler.Captured.Single().Properties["p"].Should().BeOfType<string>()
            .Which.Should().Contain("intentional failure");
    }

    private object Run(WorkflowDefinition definition, params IWorkflowModule[] modules)
        => this.Run(definition, new Dictionary<string, object?>(), modules);

    private object Run(WorkflowDefinition definition, Dictionary<string, object?> inputs, params IWorkflowModule[] modules)
    {
        var services = FindingDefs.Services(modules, s =>
        {
            s.AddSingleton<IExpressionEvaluator>(new JintExpressionEvaluator(NullLogger<JintExpressionEvaluator>.Instance));
            s.AddSingleton<IPropertyBinder>(sp => new PropertyBinder(sp.GetService<IExpressionEvaluator>()));
        });
        var id = Guid.NewGuid();
        var parent = this.CreateTestProbe();
        var executor = parent.ChildActorOf(WorkflowExecutor.Props(id, definition, inputs, services));
        executor.Tell(new StartExecution(id));
        return parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, FindingDefs.RunTimeout);
    }
}
