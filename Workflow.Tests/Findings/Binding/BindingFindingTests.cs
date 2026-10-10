// <copyright file="BindingFindingTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Findings.Binding;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.TestKit;
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
using Xunit;

/// <summary>
/// Tests for the data-binding findings in <c>docs/architecture/02-data-binding-and-variables.md §10</c>.
/// Each asserts the behaviour the code SHOULD have. In-memory actors and stubs only.
/// </summary>
[Trait("Category", FindingDefs.Category)]
public sealed class BindingFindingTests : TestKit
{
    /// <summary>Registers a real PropertyBinder + Jint evaluator so {{…}} resolves during a run.</summary>
    private static void AddBinding(IServiceCollection services)
    {
        services.AddSingleton<IExpressionEvaluator>(new JintExpressionEvaluator(NullLogger<JintExpressionEvaluator>.Instance));
        services.AddSingleton<IPropertyBinder>(sp => new PropertyBinder(sp.GetService<IExpressionEvaluator>()));
    }

    private (IActorRef Executor, TestProbe Parent, Guid Id) Run(
        WorkflowDefinition definition,
        IServiceProvider services,
        Dictionary<string, object?>? inputs = null,
        [System.Runtime.CompilerServices.CallerMemberName] string name = "run")
    {
        var id = Guid.NewGuid();
        var parent = this.CreateTestProbe(name + "-parent");
        var executor = parent.ChildActorOf(
            WorkflowExecutor.Props(id, definition, inputs ?? new Dictionary<string, object?>(), services),
            name + "-exec");
        executor.Tell(new StartExecution(id));
        return (executor, parent, id);
    }

    // ── Finding 02 §10.1 — {{nodeId.port}} only resolves direct predecessors ─────────────────────

    /// <summary>
    /// A property referencing a NON-adjacent upstream node's output (<c>{{src.value}}</c>) should
    /// resolve. <c>docs/variables.md</c> says node-output refs are "the only way to reach nodes
    /// further upstream", but the binder only sees direct predecessors, so the reference is
    /// unresolvable and fails the node. The run should complete with the captured value.
    /// </summary>
    [Fact]
    public void NonAdjacentNodeOutputReference_ShouldResolve()
    {
        var src = new OutputModule("f.src", new Dictionary<string, object?> { ["value"] = "from-src" });
        var mid = new PassModule("f.mid");
        var sink = new CaptureModule("f.sink", templatedProperties: new[] { "p" });
        var services = FindingDefs.Services(
            new IWorkflowModule[] { src, mid, sink },
            AddBinding);

        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("src", "f.src"),
                FindingDefs.Node("mid", "f.mid"),
                FindingDefs.Node("sink", "f.sink", properties: new (string, object?)[] { ("p", "{{src.value}}") }),
            },
            connections: new[]
            {
                FindingDefs.Edge("src", "value", "mid", "input"),
                FindingDefs.Edge("mid", "output", "sink", "input"),
            });

        var (_, parent, _) = this.Run(definition, services);
        var terminal = parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, FindingDefs.RunTimeout);

        using var _ = new FluentAssertions.Execution.AssertionScope();
        terminal.Should().BeOfType<WorkflowCompleted>(
            because: "a reference to a non-adjacent upstream output should resolve, per docs/variables.md (02 §10.1)");
        sink.Captured.TryDequeue(out var call).Should().BeTrue();
        call!.Properties.Should().ContainKey("p").WhoseValue.Should().Be("from-src");
    }

    // ── Finding 02 §10.2 — input keys shadow variables of the same name ──────────────────────────

    /// <summary>
    /// A declared variable <c>count = 99</c> should win when a property reads <c>{{Variable.count}}</c>,
    /// even if the node also has an input PORT named <c>count</c> carrying <c>7</c>. Today the plain
    /// input key is overlaid onto the variable map, so the reference resolves to <c>7</c>.
    /// </summary>
    [Fact]
    public void InputKey_ShouldNotShadow_DeclaredVariable()
    {
        var src = new OutputModule("f.vsrc", new Dictionary<string, object?> { ["value"] = 7 });
        var sink = new CaptureModule("f.vsink", inputPorts: new[] { "count" }, templatedProperties: new[] { "p" });
        var services = FindingDefs.Services(new IWorkflowModule[] { src, sink }, AddBinding);

        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("src", "f.vsrc"),
                FindingDefs.Node("sink", "f.vsink", properties: new (string, object?)[] { ("p", "{{Variable.count}}") }),
            },
            connections: new[] { FindingDefs.Edge("src", "value", "sink", "count") },
            variables: new[]
            {
                new VariableDefinition("count", PropertyType.Int, FindingDefs.Json(99)),
            });

        var (_, parent, _) = this.Run(definition, services);
        parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, FindingDefs.RunTimeout);

        sink.Captured.TryDequeue(out var call).Should().BeTrue();
        call!.Properties.Should().ContainKey("p").WhoseValue.Should().Be(
            99,
            because: "{{Variable.count}} should read the declared variable (99), not the input port value (7) (02 §10.2)");
    }
}
