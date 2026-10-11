// <copyright file="NestedConstructTests.cs" company="GlutenFree">
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
using Workflow.Core.Abstractions;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Flow;
using Xunit;

/// <summary>
/// §12.10: a construct (loop, parallel, try/catch) nested inside another construct's body runs as a
/// construct. <see cref="SubGraphExecutor"/> spawns it through the shared
/// <see cref="ConstructSpawnCore"/>, so its body runs once per its own iteration / branch, its done
/// port fires once when it finishes, break binds to the innermost loop, and its failure reaches the
/// enclosing body's error handling (an outer try/catch).
/// </summary>
/// <remarks>Uses the real built-in construct modules plus scripted stubs; in-memory actors only.</remarks>
public class NestedConstructTests : TestKit
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// foreach [1,2,3] → body: foreach [a,b] → body: leaf. The leaf runs 3 × 2 = 6 times, the inner
    /// done successor once per outer iteration, and the outer done successor once.
    /// </summary>
    [Fact]
    public void ForeachNestedInForeachBody_RunsInnerBodyItemsTimesOuterItems()
    {
        var leaf = Pass("nc.leaf");
        var innerDone = Pass("nc.innerdone");
        var after = Pass("nc.after");

        var definition = Workflow(
            new[]
            {
                Foreach("outer", 1, 2, 3),
                Foreach("inner", "a", "b"),
                Node("leaf", "nc.leaf"),
                Node("innerDone", "nc.innerdone"),
                Node("after", "nc.after"),
            },
            new[]
            {
                Edge("outer", "loopBody", "inner"),
                Edge("inner", "loopBody", "leaf"),
                Edge("inner", "done", "innerDone"),
                Edge("outer", "done", "after"),
            });

        var terminal = this.Run(definition, new ForEachModule(), leaf, innerDone, after);

        terminal.Should().BeOfType<WorkflowCompleted>();
        leaf.Calls.Should().Be(6, because: "the inner loop iterates its 2 items in each of the 3 outer iterations");
        innerDone.Calls.Should().Be(3, because: "the inner loop's done port fires once per outer iteration");
        after.Calls.Should().Be(1);
    }

    /// <summary>
    /// foreach [1,2,3] → body: trycatch (try: always-failing node, catch: handler). Each iteration's
    /// failure is caught, so the handler runs 3 times and the loop (and run) completes.
    /// </summary>
    [Fact]
    public void TryCatchInsideForeachBody_CatchesPerIteration_AndLoopCompletes()
    {
        var boom = new ScriptedModule("nc.boom", (_, _) => Fail());
        var handler = Pass("nc.handler");
        var after = Pass("nc.aftertc");

        var definition = Workflow(
            new[]
            {
                Foreach("loop", 1, 2, 3),
                Node("tc", "builtin.trycatch"),
                Node("boom", "nc.boom"),
                Node("handler", "nc.handler"),
                Node("after", "nc.aftertc"),
            },
            new[]
            {
                Edge("loop", "loopBody", "tc"),
                Edge("tc", "try", "boom"),
                Edge("tc", "catch", "handler"),
                Edge("loop", "done", "after"),
            });

        var terminal = this.Run(definition, new ForEachModule(), new TryCatchModule(), boom, handler, after);

        terminal.Should().BeOfType<WorkflowCompleted>(
            because: "the try/catch inside the body catches each iteration's failure, so the loop completes");
        boom.Calls.Should().Be(3);
        handler.Calls.Should().Be(3, because: "the catch body runs once per caught failure");
        after.Calls.Should().Be(1);
    }

    /// <summary>
    /// foreach [1,2] → body: parallel (b0 → slow left, b1 → right, done → joined). Both branches run
    /// in every outer iteration and the parallel's done successor runs once per iteration, only after
    /// the slow branch finished.
    /// </summary>
    [Fact]
    public void ParallelInsideForeachBody_RunsEveryBranchPerIteration_ThenDone()
    {
        var order = new ConcurrentQueue<string>();
        var left = new ScriptedModule("nc.left", (_, _) => { order.Enqueue("left"); return Ok(); }, delayMs: 200);
        var right = Pass("nc.right");
        var joined = new ScriptedModule("nc.joined", (_, _) => { order.Enqueue("joined"); return Ok(); });
        var after = Pass("nc.afterpar");

        var definition = Workflow(
            new[]
            {
                Foreach("loop", 1, 2),
                Node("par", "builtin.parallel", properties: new (string, object?)[] { ("branches", new[] { "b0", "b1" }) }),
                Node("left", "nc.left"),
                Node("right", "nc.right"),
                Node("joined", "nc.joined"),
                Node("after", "nc.afterpar"),
            },
            new[]
            {
                Edge("loop", "loopBody", "par"),
                Edge("par", "b0", "left"),
                Edge("par", "b1", "right"),
                Edge("par", "done", "joined"),
                Edge("loop", "done", "after"),
            });

        var terminal = this.Run(definition, new ForEachModule(), new ParallelModule(), left, right, joined, after);

        terminal.Should().BeOfType<WorkflowCompleted>();
        left.Calls.Should().Be(2);
        right.Calls.Should().Be(2);
        joined.Calls.Should().Be(2, because: "the parallel's done port fires once per outer iteration");
        order.Should().Equal(new[] { "left", "joined", "left", "joined" },
            because: "the done port fires only when every branch has finished");
        after.Calls.Should().Be(1);
    }

    /// <summary>
    /// trycatch → try: foreach [1,2,3] → body: leaf; foreach done → loopDone. The loop inside the try
    /// iterates all its items, nothing is caught, and the try/catch's done successor runs once.
    /// </summary>
    [Fact]
    public void ForeachInsideTryBody_IteratesAllItems()
    {
        var leaf = Pass("nc.tryleaf");
        var loopDone = Pass("nc.loopdone");
        var handler = Pass("nc.unusedhandler");
        var after = Pass("nc.aftertry");

        var definition = Workflow(
            new[]
            {
                Node("tc", "builtin.trycatch"),
                Foreach("loop", 1, 2, 3),
                Node("leaf", "nc.tryleaf"),
                Node("loopDone", "nc.loopdone"),
                Node("handler", "nc.unusedhandler"),
                Node("after", "nc.aftertry"),
            },
            new[]
            {
                Edge("tc", "try", "loop"),
                Edge("loop", "loopBody", "leaf"),
                Edge("loop", "done", "loopDone"),
                Edge("tc", "catch", "handler"),
                Edge("tc", "done", "after"),
            });

        var terminal = this.Run(definition, new ForEachModule(), new TryCatchModule(), leaf, loopDone, handler, after);

        terminal.Should().BeOfType<WorkflowCompleted>();
        leaf.Calls.Should().Be(3, because: "the loop inside the try body iterates every item");
        loopDone.Calls.Should().Be(1);
        handler.Calls.Should().Be(0);
        after.Calls.Should().Be(1);
    }

    /// <summary>
    /// foreach [1,2,3] → body: foreach [a,b,c] → body: leaf → break. The break ends only the inner
    /// loop (after its first item); the outer loop still runs all 3 iterations.
    /// </summary>
    [Fact]
    public void BreakInInnerLoop_EndsOnlyTheInnerLoop()
    {
        var leaf = Pass("nc.breakleaf");
        var innerDone = Pass("nc.breakinnerdone");
        var after = Pass("nc.breakafter");

        var definition = Workflow(
            new[]
            {
                Foreach("outer", 1, 2, 3),
                Foreach("inner", "a", "b", "c"),
                Node("leaf", "nc.breakleaf"),
                Node("brk", "builtin.break"),
                Node("innerDone", "nc.breakinnerdone"),
                Node("after", "nc.breakafter"),
            },
            new[]
            {
                Edge("outer", "loopBody", "inner"),
                Edge("inner", "loopBody", "leaf"),
                Edge("leaf", "output", "brk"),
                Edge("inner", "done", "innerDone"),
                Edge("outer", "done", "after"),
            });

        var terminal = this.Run(definition, new ForEachModule(), new BreakModule(), leaf, innerDone, after);

        terminal.Should().BeOfType<WorkflowCompleted>();
        leaf.Calls.Should().Be(3, because: "each inner loop breaks after its first item");
        innerDone.Calls.Should().Be(3, because: "the break must not reach the outer loop, which runs all 3 iterations");
        after.Calls.Should().Be(1);
    }

    /// <summary>
    /// foreach [1,2,3] → body: trycatch → try: leaf → break. The break inside the try branch ends the
    /// enclosing loop after its first iteration (the try/catch carries the flag out of its branch).
    /// </summary>
    [Fact]
    public void BreakInTryBranchInsideLoop_EndsTheLoop()
    {
        var leaf = Pass("nc.trybreakleaf");
        var after = Pass("nc.trybreakafter");

        var definition = Workflow(
            new[]
            {
                Foreach("loop", 1, 2, 3),
                Node("tc", "builtin.trycatch"),
                Node("leaf", "nc.trybreakleaf"),
                Node("brk", "builtin.break"),
                Node("after", "nc.trybreakafter"),
            },
            new[]
            {
                Edge("loop", "loopBody", "tc"),
                Edge("tc", "try", "leaf"),
                Edge("leaf", "output", "brk"),
                Edge("loop", "done", "after"),
            });

        var terminal = this.Run(definition, new ForEachModule(), new TryCatchModule(), new BreakModule(), leaf, after);

        terminal.Should().BeOfType<WorkflowCompleted>();
        leaf.Calls.Should().Be(1, because: "the break in the try branch ends the loop after its first iteration");
        after.Calls.Should().Be(1);
    }

    /// <summary>
    /// foreach [1,2,3] → body: trycatch (try: failing node, catch: handler → break). The break in the
    /// catch branch ends the loop after the first caught failure.
    /// </summary>
    [Fact]
    public void BreakInCatchBranchInsideLoop_EndsTheLoop()
    {
        var boom = new ScriptedModule("nc.catchbreakboom", (_, _) => Fail());
        var handler = Pass("nc.catchbreakhandler");
        var after = Pass("nc.catchbreakafter");

        var definition = Workflow(
            new[]
            {
                Foreach("loop", 1, 2, 3),
                Node("tc", "builtin.trycatch"),
                Node("boom", "nc.catchbreakboom"),
                Node("handler", "nc.catchbreakhandler"),
                Node("brk", "builtin.break"),
                Node("after", "nc.catchbreakafter"),
            },
            new[]
            {
                Edge("loop", "loopBody", "tc"),
                Edge("tc", "try", "boom"),
                Edge("tc", "catch", "handler"),
                Edge("handler", "output", "brk"),
                Edge("loop", "done", "after"),
            });

        var terminal = this.Run(definition, new ForEachModule(), new TryCatchModule(), new BreakModule(), boom, handler, after);

        terminal.Should().BeOfType<WorkflowCompleted>();
        boom.Calls.Should().Be(1, because: "the break in the catch branch ends the loop after its first iteration");
        handler.Calls.Should().Be(1);
        after.Calls.Should().Be(1);
    }

    /// <summary>
    /// foreach [1,2,3] → body: parallel (b0: leaf → break, b1: right). The break in a branch ends the
    /// loop once the parallel completes; both branches of that one iteration still run.
    /// </summary>
    [Fact]
    public void BreakInParallelBranchInsideLoop_EndsTheLoop()
    {
        var leaf = Pass("nc.parbreakleaf");
        var right = Pass("nc.parbreakright");
        var after = Pass("nc.parbreakafter");

        var definition = Workflow(
            new[]
            {
                Foreach("loop", 1, 2, 3),
                Node("par", "builtin.parallel", properties: new (string, object?)[] { ("branches", new[] { "b0", "b1" }) }),
                Node("leaf", "nc.parbreakleaf"),
                Node("brk", "builtin.break"),
                Node("right", "nc.parbreakright"),
                Node("after", "nc.parbreakafter"),
            },
            new[]
            {
                Edge("loop", "loopBody", "par"),
                Edge("par", "b0", "leaf"),
                Edge("leaf", "output", "brk"),
                Edge("par", "b1", "right"),
                Edge("loop", "done", "after"),
            });

        var terminal = this.Run(definition, new ForEachModule(), new ParallelModule(), new BreakModule(), leaf, right, after);

        terminal.Should().BeOfType<WorkflowCompleted>();
        leaf.Calls.Should().Be(1, because: "the break in branch b0 ends the loop after its first iteration");
        right.Calls.Should().Be(1, because: "the sibling branch of that iteration still runs");
        after.Calls.Should().Be(1);
    }

    /// <summary>
    /// foreach [1,2,3] → body: transaction → transactionBody: leaf → break. The break in the
    /// transaction body ends the loop (no transaction services: the body runs without a transaction).
    /// </summary>
    [Fact]
    public void BreakInTransactionBodyInsideLoop_EndsTheLoop()
    {
        var txn = new ScriptedModule(
            "nc.txnbreak",
            (_, _) => ModuleResult.WithTransaction(new Dictionary<string, object?>(), new TransactionRequest()),
            extraOutputs: new[] { "transactionBody", "committed", "rolledBack" });
        var leaf = Pass("nc.txnbreakleaf");
        var after = Pass("nc.txnbreakafter");

        var definition = Workflow(
            new[]
            {
                Foreach("loop", 1, 2, 3),
                Node("txn", "nc.txnbreak"),
                Node("leaf", "nc.txnbreakleaf"),
                Node("brk", "builtin.break"),
                Node("after", "nc.txnbreakafter"),
            },
            new[]
            {
                Edge("loop", "loopBody", "txn"),
                Edge("txn", "transactionBody", "leaf"),
                Edge("leaf", "output", "brk"),
                Edge("loop", "done", "after"),
            });

        var terminal = this.Run(definition, new ForEachModule(), new BreakModule(), txn, leaf, after);

        terminal.Should().BeOfType<WorkflowCompleted>();
        leaf.Calls.Should().Be(1, because: "the break in the transaction body ends the loop after its first iteration");
        after.Calls.Should().Be(1);
    }

    /// <summary>
    /// foreach outer [1,2] → body: foreach inner [a,b,c] → body: trycatch → try: leaf → break. The
    /// break travels out of the try/catch to the INNER loop only: each inner loop stops after one
    /// item, the outer loop runs both iterations.
    /// </summary>
    [Fact]
    public void BreakInTryBranchInsideInnerLoop_EndsOnlyTheInnerLoop()
    {
        var leaf = Pass("nc.innertrybreakleaf");
        var innerDone = Pass("nc.innertrybreakinnerdone");
        var after = Pass("nc.innertrybreakafter");

        var definition = Workflow(
            new[]
            {
                Foreach("outer", 1, 2),
                Foreach("inner", "a", "b", "c"),
                Node("tc", "builtin.trycatch"),
                Node("leaf", "nc.innertrybreakleaf"),
                Node("brk", "builtin.break"),
                Node("innerDone", "nc.innertrybreakinnerdone"),
                Node("after", "nc.innertrybreakafter"),
            },
            new[]
            {
                Edge("outer", "loopBody", "inner"),
                Edge("inner", "loopBody", "tc"),
                Edge("tc", "try", "leaf"),
                Edge("leaf", "output", "brk"),
                Edge("inner", "done", "innerDone"),
                Edge("outer", "done", "after"),
            });

        var terminal = this.Run(definition, new ForEachModule(), new TryCatchModule(), new BreakModule(), leaf, innerDone, after);

        terminal.Should().BeOfType<WorkflowCompleted>();
        leaf.Calls.Should().Be(2, because: "each inner loop breaks after its first item");
        innerDone.Calls.Should().Be(2, because: "the break must not cross the inner loop into the outer one");
        after.Calls.Should().Be(1);
    }

    /// <summary>
    /// trycatch → try: foreach [1,2,3] → body: node failing on its 2nd call. The inner loop fails on
    /// iteration 2 (no continueOnError) and the outer try/catch catches it: the loop's done successor
    /// never runs, the handler runs once and the run completes.
    /// </summary>
    [Fact]
    public void FailingInnerLoop_IsCaughtByOuterTryCatch()
    {
        var body = new ScriptedModule("nc.failsecond", (_, call) => call == 2 ? Fail() : Ok());
        var loopDone = Pass("nc.failloopdone");
        var handler = Pass("nc.failhandler");
        var after = Pass("nc.failafter");

        var definition = Workflow(
            new[]
            {
                Node("tc", "builtin.trycatch"),
                Foreach("loop", 1, 2, 3),
                Node("body", "nc.failsecond"),
                Node("loopDone", "nc.failloopdone"),
                Node("handler", "nc.failhandler"),
                Node("after", "nc.failafter"),
            },
            new[]
            {
                Edge("tc", "try", "loop"),
                Edge("loop", "loopBody", "body"),
                Edge("loop", "done", "loopDone"),
                Edge("tc", "catch", "handler"),
                Edge("tc", "done", "after"),
            });

        var terminal = this.Run(definition, new ForEachModule(), new TryCatchModule(), body, loopDone, handler, after);

        terminal.Should().BeOfType<WorkflowCompleted>(because: "the outer try/catch catches the inner loop's failure");
        body.Calls.Should().Be(2, because: "the loop stops at the failing iteration");
        loopDone.Calls.Should().Be(0, because: "a failed loop does not fire its done port");
        handler.Calls.Should().Be(1);
        after.Calls.Should().Be(1);
    }

    /// <summary>
    /// The Stage 3 variable rules apply recursively: a write made in an inner loop's body is seen by
    /// the next inner iteration, by later outer iterations, and after the outer loop.
    /// </summary>
    [Fact]
    public void VariableWriteInNestedLoopBody_IsVisibleAfterOuterLoop()
    {
        var counter = new ScriptedModule(
            "nc.counter",
            (ctx, _) => Ok(("n", AsInt(ctx.Variables.TryGetValue("n", out var n) ? n : null) + 1)));
        var after = Pass("nc.varafter");

        var definition = Workflow(
            new[]
            {
                Foreach("outer", 1, 2, 3),
                Foreach("inner", "a", "b"),
                Node("counter", "nc.counter"),
                Node("after", "nc.varafter"),
            },
            new[]
            {
                Edge("outer", "loopBody", "inner"),
                Edge("inner", "loopBody", "counter"),
                Edge("outer", "done", "after"),
            },
            new VariableDefinition("n", PropertyType.Int, JsonSerializer.SerializeToElement(0)));

        var terminal = this.Run(definition, new ForEachModule(), counter, after);

        terminal.Should().BeOfType<WorkflowCompleted>();
        counter.SeenVariable("n").Select(AsInt).Should().Equal(0, 1, 2, 3, 4, 5);
        after.SeenVariable("n").Select(AsInt).Should().Equal(6);
    }

    private object Run(WorkflowDefinition definition, params IWorkflowModule[] modules)
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        foreach (var module in modules)
        {
            registry.RegisterModule(module);
        }

        var services = new ServiceCollection().AddSingleton<IModuleRegistry>(registry);
        var executionId = Guid.NewGuid();
        var parent = this.CreateTestProbe();
        var executor = parent.ChildActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), services.BuildServiceProvider()));
        executor.Tell(new StartExecution(executionId));
        return parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, RunTimeout);
    }

    private static ScriptedModule Pass(string moduleId) => new(moduleId, (_, _) => Ok());

    private static NodeDefinition Foreach(string id, params object?[] items)
        => Node(id, "builtin.loop.foreach", properties: new (string, object?)[] { ("collection", items) });

    private static NodeDefinition Node(string id, string moduleId, (string Key, object? Value)[]? properties = null)
    {
        var props = HashMap<string, JsonElement>.Empty;
        foreach (var (key, value) in properties ?? Array.Empty<(string, object?)>())
        {
            props = props.AddOrUpdate(key, JsonSerializer.SerializeToElement(value));
        }

        return new NodeDefinition(id, moduleId, id, props);
    }

    private static ConnectionDefinition Edge(string from, string port, string to) => new(from, port, to, "input");

    private static WorkflowDefinition Workflow(
        NodeDefinition[] nodes,
        ConnectionDefinition[] connections,
        params VariableDefinition[] variables)
        => new(
            Id: Guid.NewGuid(),
            Name: "nested-construct-test",
            Description: null,
            Version: new Version(1, 0),
            Nodes: nodes.ToArr(),
            Connections: connections.ToArr(),
            Variables: variables.Aggregate(HashMap<string, VariableDefinition>.Empty, (map, v) => map.AddOrUpdate(v.Name, v)));

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
        _ => Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>Runs a script per call, counts calls and records the variables each call saw.</summary>
    private sealed class ScriptedModule : IWorkflowModule
    {
        private readonly Func<ModuleExecutionContext, int, ModuleResult> script;
        private readonly int delayMs;
        private readonly string[] extraOutputs;
        private readonly ConcurrentQueue<IReadOnlyDictionary<string, object?>> seen = new();
        private int calls;

        public ScriptedModule(
            string moduleId,
            Func<ModuleExecutionContext, int, ModuleResult> script,
            int delayMs = 0,
            string[]? extraOutputs = null)
        {
            this.ModuleId = moduleId;
            this.script = script;
            this.delayMs = delayMs;
            this.extraOutputs = extraOutputs ?? Array.Empty<string>();
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
            Outputs: new[] { "output" }.Concat(this.extraOutputs)
                .Select(port => PortDefinition.Create<object>(port, isRequired: false))
                .ToArr(),
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
