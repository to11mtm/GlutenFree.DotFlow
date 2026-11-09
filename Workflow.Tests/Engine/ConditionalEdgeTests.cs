// <copyright file="ConditionalEdgeTests.cs" company="GlutenFree">
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
/// Stage 7.2 — conditional edges: <see cref="ConnectionDefinition.Condition"/> decides whether an
/// edge fires (false = like an inactive port); <see cref="ConnectionDefinition.Priority"/> ≥ 1 makes
/// the edges leaving one port an if / else-if / else chain; a condition that can't be evaluated is a
/// failure of the source node. See docs/conditional-edges.md.
/// </summary>
public sealed class ConditionalEdgeTests : TestKit
{
    private static ConnectionDefinition Edge(string from, string port, string to, string? condition = null, int priority = 0, string toPort = "input")
        => new(from, port, to, toPort, condition, priority);

    private static OutputModule Source(object? value) => new("ce.src", new Dictionary<string, object?> { ["res"] = value });

    [Fact]
    public void ATrueEdgeFires_AFalseEdgeSkipsItsTarget_AndEverythingAfterIt()
    {
        var ok = new PassModule("ce.ok");
        var bad = new PassModule("ce.bad");
        var badNext = new PassModule("ce.badnext");
        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("src", "ce.src"),
                FindingDefs.Node("ok", "ce.ok"),
                FindingDefs.Node("bad", "ce.bad"),
                FindingDefs.Node("badNext", "ce.badnext"),
            },
            connections: new[]
            {
                Edge("src", "res", "ok", "value.status == 200"),
                Edge("src", "res", "bad", "value.status != 200"),
                Edge("bad", "output", "badNext"),
            });

        var terminal = this.Run(definition, Source(new Dictionary<string, object?> { ["status"] = 200 }), ok, bad, badNext);

        terminal.Should().BeOfType<WorkflowCompleted>(because: "{0}", (terminal as WorkflowFailed)?.Error.Message);
        ok.Calls.Should().Be(1);
        bad.Calls.Should().Be(0);
        badNext.Calls.Should().Be(0, because: "the skip flows downstream");
    }

    [Fact]
    public void ConditionsCanReadVariables_OtherUpstreamNodes_AndJsonValues_BracesOptional()
    {
        var a = new PassModule("ce.a");
        var b = new PassModule("ce.b");
        var c = new PassModule("ce.c");
        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("fetch", "ce.fetch"),
                FindingDefs.Node("src", "ce.src"),
                FindingDefs.Node("a", "ce.a"),
                FindingDefs.Node("b", "ce.b"),
                FindingDefs.Node("c", "ce.c"),
            },
            connections: new[]
            {
                Edge("fetch", "count", "src"),
                Edge("src", "res", "a", "Variable.limit > 5 && fetch.count > 0"),
                Edge("src", "res", "b", "{{ value.items.length == 2 }}"),
                Edge("src", "res", "c", "Variable.limit < 5"),
            },
            variables: new[] { new VariableDefinition("limit", PropertyType.Int, FindingDefs.Json(10)) });

        var terminal = this.Run(
            definition,
            new OutputModule("ce.fetch", new Dictionary<string, object?> { ["count"] = 3 }),
            Source(FindingDefs.Json(new { items = new[] { 1, 2 } })),
            a,
            b,
            c);

        terminal.Should().BeOfType<WorkflowCompleted>(because: "{0}", (terminal as WorkflowFailed)?.Error.Message);
        (a.Calls, b.Calls, c.Calls).Should().Be((1, 1, 0));
    }

    [Theory]
    [InlineData(0, 200)] // the false edge is decided first
    [InlineData(200, 0)] // the false edge is decided LAST — the old join-skip race
    public void AJoinWithAnotherActiveEdge_StillRuns_WithoutTheFalseEdgesValue(int srcDelayMs, int otherDelayMs)
    {
        var join = new CaptureModule("ce.join", inputPorts: new[] { "x", "y" });
        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("src", "ce.src"),
                FindingDefs.Node("other", "ce.other"),
                FindingDefs.Node("join", "ce.join"),
            },
            connections: new[]
            {
                Edge("src", "res", "join", "value > 100", toPort: "x"),
                Edge("other", "value", "join", toPort: "y"),
            });

        var terminal = this.Run(
            definition,
            new SlowOutput("ce.src", "res", 5, srcDelayMs),
            new SlowOutput("ce.other", "value", "from-other", otherDelayMs),
            join);

        terminal.Should().BeOfType<WorkflowCompleted>(because: "{0}", (terminal as WorkflowFailed)?.Error.Message);
        var call = join.Captured.Should().ContainSingle(because: "a join with another active edge still runs").Subject;
        call.Inputs.Should().ContainKey("y").WhoseValue.Should().Be("from-other");
        call.Inputs.GetValueOrDefault("x").Should().BeNull(because: "the false edge carries nothing (the port is just unset)");
        call.Inputs.Should().NotContainKey("src.res", because: "the false edge's source outputs aren't passed on either");
    }

    [Fact]
    public void PortRouting_AJoinDeactivatedByTheLastPredecessor_StillRuns()
    {
        // The same race with module ports (01-execution-lifecycle §12, item 6): 'other' fires into the
        // join first; 'switch' finishes later and activates a different port.
        var join = new CaptureModule("ce.join", inputPorts: new[] { "x", "y" });
        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("switch", "ce.switch"),
                FindingDefs.Node("other", "ce.other"),
                FindingDefs.Node("elsewhere", "ce.elsewhere"),
                FindingDefs.Node("join", "ce.join"),
            },
            connections: new[]
            {
                Edge("switch", "a", "join", toPort: "x"),
                Edge("switch", "b", "elsewhere"),
                Edge("other", "value", "join", toPort: "y"),
            });

        var terminal = this.Run(
            definition,
            new PortModule("ce.switch", activePorts: new[] { "b" }, declaredPorts: new[] { "a", "b" }, delayMs: 200),
            new SlowOutput("ce.other", "value", "from-other", 0),
            new PassModule("ce.elsewhere"),
            join);

        terminal.Should().BeOfType<WorkflowCompleted>(because: "{0}", (terminal as WorkflowFailed)?.Error.Message);
        join.Captured.Should().ContainSingle(because: "'other' already activated the join");
    }

    [Theory]
    [InlineData(500, "high")]
    [InlineData(50, "mid")]
    [InlineData(1, "else")]
    public void PriorityEdges_AreAnIfElseChain_AndIndependentEdgesStillFire(int value, string expected)
    {
        var high = new PassModule("ce.high");
        var mid = new PassModule("ce.mid");
        var otherwise = new PassModule("ce.else");
        var audit = new PassModule("ce.audit");
        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("src", "ce.src"),
                FindingDefs.Node("high", "ce.high"),
                FindingDefs.Node("mid", "ce.mid"),
                FindingDefs.Node("else", "ce.else"),
                FindingDefs.Node("audit", "ce.audit"),
            },
            connections: new[]
            {
                // Declared out of order on purpose: Priority decides, not position.
                Edge("src", "res", "else", priority: 3),
                Edge("src", "res", "mid", "value > 10", priority: 2),
                Edge("src", "res", "high", "value > 100", priority: 1),
                Edge("src", "res", "audit"), // priority 0: independent
            });

        var terminal = this.Run(definition, Source(value), high, mid, otherwise, audit);

        terminal.Should().BeOfType<WorkflowCompleted>(because: "{0}", (terminal as WorkflowFailed)?.Error.Message);
        new Dictionary<string, int> { ["high"] = high.Calls, ["mid"] = mid.Calls, ["else"] = otherwise.Calls }
            .Where(kv => kv.Value > 0).Select(kv => kv.Key)
            .Should().Equal(new[] { expected }, because: "only the first true edge in Priority order fires");
        audit.Calls.Should().Be(1, because: "an edge without a Priority is independent");
    }

    [Fact]
    public void OnceAPriorityEdgeWins_LaterConditionsAreNotEvaluated()
    {
        var first = new PassModule("ce.first");
        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("src", "ce.src"),
                FindingDefs.Node("first", "ce.first"),
                FindingDefs.Node("broken", "ce.broken"),
            },
            connections: new[]
            {
                Edge("src", "res", "first", "true", priority: 1),
                Edge("src", "res", "broken", "this is not an expression", priority: 2),
            });

        var terminal = this.Run(definition, Source(1), first, new PassModule("ce.broken"));

        terminal.Should().BeOfType<WorkflowCompleted>(because: "{0}", (terminal as WorkflowFailed)?.Error.Message);
        first.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("value.status ==", "couldn't be evaluated")]
    [InlineData("value.status", "must be true or false")]
    [InlineData("nosuchnode.port > 1", "couldn't be evaluated")]
    public void ABadCondition_FailsTheSourceNode_NamingTheEdge(string condition, string expected)
    {
        var next = new PassModule("ce.next");
        var definition = FindingDefs.Definition(
            nodes: new[] { FindingDefs.Node("src", "ce.src"), FindingDefs.Node("next", "ce.next") },
            connections: new[] { Edge("src", "res", "next", condition) });

        var terminal = this.Run(definition, Source(new Dictionary<string, object?> { ["status"] = 200 }), next);

        terminal.Should().BeOfType<WorkflowFailed>()
            .Which.Error.Message.Should().Contain("src.res → next.input").And.Contain(expected);
        next.Calls.Should().Be(0);
    }

    [Fact]
    public void ABadCondition_GoesThroughTheSourceNodesOnErrorSetting()
    {
        var handler = new PassModule("ce.handler");
        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("src", "ce.src", errorHandling: new ErrorHandling(ErrorBehavior.UseErrorHandler, "h")),
                FindingDefs.Node("next", "ce.next"),
                FindingDefs.Node("h", "ce.handler"),
            },
            connections: new[] { Edge("src", "res", "next", "value.status ==") });

        var terminal = this.Run(definition, Source(1), new PassModule("ce.next"), handler);

        terminal.Should().BeOfType<WorkflowCompleted>(because: "{0}", (terminal as WorkflowFailed)?.Error.Message);
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public void ConditionsWorkInsideALoopBody()
    {
        var even = new PassModule("ce.even");
        var odd = new PassModule("ce.odd");
        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("loop", "builtin.loop.foreach", properties: new (string, object?)[] { ("collection", new[] { 1, 2, 3, 4, 5 }) }),
                FindingDefs.Node("pick", "ce.pick"),
                FindingDefs.Node("even", "ce.even"),
                FindingDefs.Node("odd", "ce.odd"),
            },
            connections: new[]
            {
                Edge("loop", "loopBody", "pick"),
                Edge("pick", "output", "even", "Variable.item % 2 == 0"),
                Edge("pick", "output", "odd", "Variable.item % 2 == 1"),
            });

        var terminal = this.Run(definition, new ForEachModule(), new PassModule("ce.pick"), even, odd);

        terminal.Should().BeOfType<WorkflowCompleted>(because: "{0}", (terminal as WorkflowFailed)?.Error.Message);
        (even.Calls, odd.Calls).Should().Be((2, 3));
    }

    /// <summary>Emits one output after a delay (to order completions deterministically).</summary>
    private sealed class SlowOutput : IWorkflowModule
    {
        private readonly string port;
        private readonly object? value;
        private readonly int delayMs;

        public SlowOutput(string id, string port, object? value, int delayMs)
        {
            this.ModuleId = id;
            this.port = port;
            this.value = value;
            this.delayMs = delayMs;
        }

        public string ModuleId { get; }

        public string DisplayName => this.ModuleId;

        public string Category => "Test";

        public string Description => "delayed output";

        public string Icon => "🐢";

        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            LanguageExt.Arr.create(PortDefinition.Create<object>("input", isRequired: false)),
            LanguageExt.Arr.create(PortDefinition.Create<object>(this.port, isRequired: false)),
            LanguageExt.Arr<ModulePropertyDefinition>.Empty);

        public async System.Threading.Tasks.Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, System.Threading.CancellationToken cancellationToken = default)
        {
            await System.Threading.Tasks.Task.Delay(this.delayMs, cancellationToken);
            return ModuleResult.Ok(new Dictionary<string, object?> { [this.port] = this.value });
        }
    }

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
}
