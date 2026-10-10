// <copyright file="SubGraphParityTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Flow;
using Xunit;

/// <summary>
/// Construct bodies (loop iterations, parallel branches, try/catch bodies) behave like top-level
/// nodes: per-node retry/continue is honoured (§12.4) and variable writes propagate (§10.3) under
/// the agreed scoping rules — sequential bodies see earlier writes and report them after the
/// construct; parallel branches are isolated and merged in branch order; writes made before a
/// failure are kept.
/// </summary>
public class SubGraphParityTests : TestKit
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(15);

    // ── §12.4: per-node error handling inside bodies ─────────────────────────────────────────────

    [Fact]
    public void RetryInsideLoopBody_IsHonoured()
    {
        var body = new ScriptedModule("sp.flaky", (_, call) => call == 1 ? Fail() : Ok());
        var definition = Workflow(
            new[]
            {
                Foreach("loop", 1),
                Node("body", "sp.flaky", errorHandling: new ErrorHandling(ErrorBehavior.Retry), retry: QuickRetry(3)),
            },
            new[] { Edge("loop", "loopBody", "body") });

        var terminal = this.Run(definition, body);

        terminal.Should().BeOfType<WorkflowCompleted>(
            because: "a Retry-configured body node that recovers on attempt 2 should let the loop complete (§12.4)");
        body.Calls.Should().Be(2);
    }

    [Fact]
    public void RetryExhaustedInsideLoopBody_FailsTheRun()
    {
        var body = new ScriptedModule("sp.alwaysfails", (_, _) => Fail());
        var definition = Workflow(
            new[]
            {
                Foreach("loop", 1),
                Node("body", "sp.alwaysfails", errorHandling: new ErrorHandling(ErrorBehavior.Retry), retry: QuickRetry(2)),
            },
            new[] { Edge("loop", "loopBody", "body") });

        var terminal = this.Run(definition, body);

        terminal.Should().BeOfType<WorkflowFailed>();
        body.Calls.Should().Be(2, because: "MaxAttempts=2 means the first run plus one retry");
    }

    [Fact]
    public void ContinueInsideLoopBody_RoutesToTheNextBodyNode()
    {
        var failing = new ScriptedModule("sp.failcontinue", (_, _) => Fail());
        var next = new ScriptedModule("sp.next", (_, _) => Ok());
        var definition = Workflow(
            new[]
            {
                Foreach("loop", 3),
                Node("first", "sp.failcontinue", errorHandling: new ErrorHandling(ErrorBehavior.Continue)),
                Node("second", "sp.next"),
            },
            new[] { Edge("loop", "loopBody", "first"), Edge("first", "output", "second") });

        var terminal = this.Run(definition, failing, next);

        terminal.Should().BeOfType<WorkflowCompleted>(because: "Continue-on-error inside a body must not fail the loop (§12.4)");
        next.Calls.Should().Be(3, because: "the body continues past the failed node in every iteration");
    }

    // ── §10.3: variable writes in sequential loops ───────────────────────────────────────────────

    [Fact]
    public void VariableWriteInsideLoopBody_IsVisibleAfterLoop()
    {
        var body = new ScriptedModule("sp.setlast", (ctx, _) => Ok(writes: ("lastItem", ctx.Inputs["item"])));
        var after = new ScriptedModule("sp.after", (_, _) => Ok());
        var definition = Workflow(
            new[] { Foreach("loop", new object?[] { "a", "b", "c" }), Node("body", "sp.setlast"), Node("after", "sp.after") },
            new[] { Edge("loop", "loopBody", "body"), Edge("loop", "done", "after") },
            new VariableDefinition("lastItem", PropertyType.String, JsonSerializer.SerializeToElement("none")));

        var terminal = this.Run(definition, body, after);

        terminal.Should().BeOfType<WorkflowCompleted>();
        after.SeenVariable("lastItem").Should().ContainSingle()
            .Which.Should().Be("c", because: "the last iteration's write is visible after the loop (§10.3)");
    }

    [Fact]
    public void LaterIterations_SeeEarlierIterationsWrites()
    {
        var body = new ScriptedModule("sp.increment", (ctx, _) => Ok(writes: ("count", AsInt(ctx.Variables["count"]) + 1)));
        var after = new ScriptedModule("sp.after2", (_, _) => Ok());
        var definition = Workflow(
            new[] { Foreach("loop", 3), Node("body", "sp.increment"), Node("after", "sp.after2") },
            new[] { Edge("loop", "loopBody", "body"), Edge("loop", "done", "after") },
            new VariableDefinition("count", PropertyType.Int, JsonSerializer.SerializeToElement(0)));

        var terminal = this.Run(definition, body, after);

        terminal.Should().BeOfType<WorkflowCompleted>();
        body.SeenVariable("count").Select(AsInt).Should().Equal(new[] { 0, 1, 2 },
            because: "each iteration sees the previous iterations' writes (sequential loop scoping)");
        after.SeenVariable("count").Select(AsInt).Should().Equal(new[] { 3 });
    }

    [Fact]
    public void WhileLoop_DrivenByABodyVariableWrite_Terminates()
    {
        var body = new ScriptedModule("sp.whilebody", (ctx, _) => Ok(writes: ("count", AsInt(ctx.Variables["count"]) + 1)));
        var after = new ScriptedModule("sp.afterwhile", (_, _) => Ok());
        var whileProps = new (string, object?)[] { ("condition", "count < 3"), ("maxIterations", 10) };
        var definition = Workflow(
            new[] { Node("loop", "builtin.loop.while", properties: whileProps), Node("body", "sp.whilebody"), Node("after", "sp.afterwhile") },
            new[] { Edge("loop", "loopBody", "body"), Edge("loop", "done", "after") },
            new VariableDefinition("count", PropertyType.Int, JsonSerializer.SerializeToElement(0)));

        var terminal = this.Run(definition, new IWorkflowModule[] { new WhileModule(), body, after }, withExpressions: true);

        terminal.Should().BeOfType<WorkflowCompleted>(
            because: "the while condition reads the body's write, so the loop ends (WhileModule's documented contract)");
        body.Calls.Should().Be(3);
        after.SeenVariable("count").Select(AsInt).Should().Equal(new[] { 3 });
    }

    // ── §10.3: parallel branches — isolated, merged in branch order ──────────────────────────────

    [Fact]
    public void ParallelBranchWrites_AreIsolated_AndMergedInBranchOrder()
    {
        // Branch 0 is slow and finishes LAST; branch 1 writes first. Merge is by branch index, not
        // timing, so branch 1 wins. Branch 0's reader runs after branch 1 already wrote, and must
        // see only its own branch's write (isolation).
        var slow = new ScriptedModule("sp.slow", (_, _) => Ok(writes: ("winner", "branch0")), delayMs: 300);
        var slowReader = new ScriptedModule("sp.slowreader", (_, _) => Ok());
        var fast = new ScriptedModule("sp.fast", (_, _) => Ok(writes: ("winner", "branch1")));
        var after = new ScriptedModule("sp.afterpar", (_, _) => Ok());
        var parProps = new (string, object?)[] { ("branches", new[] { "b0", "b1" }) };
        var definition = Workflow(
            new[]
            {
                Node("par", "builtin.parallel", properties: parProps),
                Node("reader0", "sp.slowreader"),
                Node("slow0", "sp.slow"),
                Node("fast1", "sp.fast"),
                Node("after", "sp.afterpar"),
            },
            new[]
            {
                Edge("par", "b0", "slow0"),
                Edge("slow0", "output", "reader0"),
                Edge("par", "b1", "fast1"),
                Edge("par", "done", "after"),
            },
            new VariableDefinition("winner", PropertyType.String, JsonSerializer.SerializeToElement("none")));

        var terminal = this.Run(definition, new IWorkflowModule[] { new ParallelModule(), slow, slowReader, fast, after });

        terminal.Should().BeOfType<WorkflowCompleted>();
        slowReader.SeenVariable("winner").Should().Equal(new[] { "branch0" },
            because: "a branch sees its own writes but never a sibling's (branch 1 wrote earlier)");
        after.SeenVariable("winner").Should().Equal(new[] { "branch1" },
            because: "writes merge in branch order, so the highest branch index wins regardless of finish order");
    }

    // ── §10.3: try/catch — writes made before a failure are kept ─────────────────────────────────

    [Fact]
    public void TryCatch_WritesBeforeFailure_AreVisibleToCatch_AndAfter()
    {
        var tryWrite = new ScriptedModule("sp.trywrite", (_, _) => Ok(writes: ("stage", "try")));
        var tryFail = new ScriptedModule("sp.tryfail", (_, _) => Fail());
        var catchNode = new ScriptedModule("sp.catch", (_, _) => Ok(writes: ("caught", true)));
        var after = new ScriptedModule("sp.aftertc", (_, _) => Ok());
        var definition = Workflow(
            new[]
            {
                Node("tc", "builtin.trycatch"),
                Node("t1", "sp.trywrite"),
                Node("t2", "sp.tryfail"),
                Node("c1", "sp.catch"),
                Node("after", "sp.aftertc"),
            },
            new[]
            {
                Edge("tc", "try", "t1"),
                Edge("t1", "output", "t2"),
                Edge("tc", "catch", "c1"),
                Edge("tc", "done", "after"),
            },
            new VariableDefinition("stage", PropertyType.String, JsonSerializer.SerializeToElement("none")),
            new VariableDefinition("caught", PropertyType.Boolean, JsonSerializer.SerializeToElement(false)));

        var terminal = this.Run(definition, new IWorkflowModule[] { new TryCatchModule(), tryWrite, tryFail, catchNode, after });

        terminal.Should().BeOfType<WorkflowCompleted>();
        catchNode.SeenVariable("stage").Should().Equal(new[] { "try" }, because: "catch sees the try body's writes made before it failed");
        after.SeenVariable("stage").Should().Equal(new[] { "try" });
        after.SeenVariable("caught").Should().Equal(new object?[] { true });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private object Run(WorkflowDefinition definition, params ScriptedModule[] modules)
        => this.Run(definition, new IWorkflowModule[] { new ForEachModule() }.Concat(modules).ToArray());

    private object Run(WorkflowDefinition definition, IWorkflowModule[] modules, bool withExpressions = false)
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        foreach (var module in modules)
        {
            registry.RegisterModule(module);
        }

        var services = new ServiceCollection().AddSingleton<IModuleRegistry>(registry);
        if (withExpressions)
        {
            services.AddSingleton<IExpressionEvaluator>(new JintExpressionEvaluator(NullLogger<JintExpressionEvaluator>.Instance));
        }

        var executionId = Guid.NewGuid();
        var parent = this.CreateTestProbe();
        var executor = parent.ChildActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), services.BuildServiceProvider()));
        executor.Tell(new StartExecution(executionId));
        return parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, RunTimeout);
    }

    private static NodeDefinition Foreach(string id, int count)
        => Foreach(id, Enumerable.Range(1, count).Cast<object?>().ToArray());

    private static NodeDefinition Foreach(string id, object?[] items)
        => Node(id, "builtin.loop.foreach", properties: new (string, object?)[] { ("collection", items) });

    private static NodeDefinition Node(
        string id,
        string moduleId,
        ErrorHandling? errorHandling = null,
        RetryPolicy? retry = null,
        (string Key, object? Value)[]? properties = null)
    {
        var props = HashMap<string, JsonElement>.Empty;
        foreach (var (key, value) in properties ?? Array.Empty<(string, object?)>())
        {
            props = props.AddOrUpdate(key, JsonSerializer.SerializeToElement(value));
        }

        return new NodeDefinition(id, moduleId, id, props, ErrorHandling: errorHandling, RetryPolicy: retry);
    }

    private static ConnectionDefinition Edge(string from, string port, string to) => new(from, port, to, "input");

    private static WorkflowDefinition Workflow(
        NodeDefinition[] nodes,
        ConnectionDefinition[] connections,
        params VariableDefinition[] variables)
        => new(
            Id: Guid.NewGuid(),
            Name: "subgraph-parity-test",
            Description: null,
            Version: new Version(1, 0),
            Nodes: nodes.ToArr(),
            Connections: connections.ToArr(),
            Variables: variables.Aggregate(HashMap<string, VariableDefinition>.Empty, (map, v) => map.AddOrUpdate(v.Name, v)));

    private static RetryPolicy QuickRetry(int maxAttempts)
        => new(MaxAttempts: maxAttempts, DelayMs: 10, BackoffMultiplier: 1.0, MaxDelayMs: 50);

    private static ModuleResult Ok(params (string Name, object? Value)[] writes)
        => writes.Length == 0
            ? ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = "ok" })
            : ModuleResult.Ok(
                new Dictionary<string, object?> { ["output"] = "ok" },
                writes.ToDictionary(w => w.Name, w => w.Value));

    private static ModuleResult Fail() => ModuleResult.Fail("intentional failure", new InvalidOperationException("intentional failure"));

    private static int AsInt(object? value) => value switch
    {
        JsonElement json => json.GetInt32(),
        null => 0,
        _ => Convert.ToInt32(value),
    };

    /// <summary>Runs a script per call, counts calls and records the variables each call saw.</summary>
    private sealed class ScriptedModule : IWorkflowModule
    {
        private readonly Func<ModuleExecutionContext, int, ModuleResult> script;
        private readonly int delayMs;
        private readonly ConcurrentQueue<IReadOnlyDictionary<string, object?>> seen = new();
        private int calls;

        public ScriptedModule(string moduleId, Func<ModuleExecutionContext, int, ModuleResult> script, int delayMs = 0)
        {
            this.ModuleId = moduleId;
            this.script = script;
            this.delayMs = delayMs;
        }

        public int Calls => this.calls;

        public string ModuleId { get; }

        public string DisplayName => this.ModuleId;

        public string Category => "Test";

        public string Description => "Scripted test module";

        public string Icon => "📜";

        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Inputs: new[] { PortDefinition.Create<object>("input", isRequired: false) }.ToArr(),
            Outputs: new[] { PortDefinition.Create<object>("output", isRequired: false) }.ToArr(),
            Properties: Arr<ModulePropertyDefinition>.Empty);

        /// <summary>Gets the value of <paramref name="name"/> each call saw in its variables, in call order.</summary>
        public IReadOnlyList<object?> SeenVariable(string name)
            => this.seen.Select(v => v.TryGetValue(name, out var value) ? value : null).ToList();

        public async Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref this.calls);
            this.seen.Enqueue(new Dictionary<string, object?>(context.Variables ?? new Dictionary<string, object?>()));
            if (this.delayMs > 0)
            {
                await Task.Delay(this.delayMs, cancellationToken).ConfigureAwait(false);
            }

            return this.script(context, call);
        }
    }
}
