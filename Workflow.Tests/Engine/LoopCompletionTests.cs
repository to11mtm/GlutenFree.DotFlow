// <copyright file="LoopCompletionTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

using System;
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
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Flow;
using Xunit;

/// <summary>
/// End-to-end tests for loop completion in <see cref="WorkflowExecutor"/>: when a loop finishes,
/// nodes wired to its <c>done</c> port must run, and their outputs must reach the workflow outputs.
/// Uses the real <c>builtin.loop.foreach</c> module.
/// </summary>
/// <remarks>
/// Regression guard for §12.9: <c>HandleLoopCompleted</c> used to seed the body scope from every
/// outgoing edge of the loop node, so the done-port node was swallowed into the body and skipped.
/// </remarks>
public class LoopCompletionTests : TestKit
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// A node wired to a foreach loop's <c>done</c> port runs after the loop finishes.
    /// </summary>
    [Fact]
    public void LoopDonePort_ShouldRunPostLoopNode()
    {
        var body = new CountingModule("lc.body", "output", "body-ran");
        var after = new CountingModule("lc.after", "output", "after-ran");
        var services = Services(new ForEachModule(), body, after);

        var definition = Definition(
            nodes: new[]
            {
                Node("loop", "builtin.loop.foreach", ("collection", new object?[] { "a", "b", "c" })),
                Node("body", "lc.body"),
                Node("after", "lc.after"),
            },
            connections: new[]
            {
                new ConnectionDefinition("loop", "loopBody", "body", "input"),
                new ConnectionDefinition("loop", "done", "after", "input"),
            });

        var terminal = this.Run(definition, services);

        terminal.Should().BeOfType<WorkflowCompleted>(because: "the loop should finish and the run complete");
        after.Calls.Should().Be(1,
            because: "the node on the loop's done port should run once after the loop, not be swallowed into the body scope (§12.9)");
    }

    /// <summary>
    /// A foreach with a real body and a real done node: the body runs once per item, and the done
    /// node's output shows up in the workflow's outputs.
    /// </summary>
    [Fact]
    public void ForEachWithBodyAndDoneNode_DoneNodeContributesToWorkflowOutputs()
    {
        var body = new CountingModule("lc.body2", "output", "body-ran");
        var after = new CountingModule("lc.after2", "summary", "loop-finished");
        var services = Services(new ForEachModule(), body, after);

        var definition = Definition(
            nodes: new[]
            {
                Node("loop", "builtin.loop.foreach", ("collection", new object?[] { 1, 2, 3 })),
                Node("body", "lc.body2"),
                Node("after", "lc.after2"),
            },
            connections: new[]
            {
                new ConnectionDefinition("loop", "loopBody", "body", "input"),
                new ConnectionDefinition("loop", "done", "after", "input"),
            });

        var terminal = this.Run(definition, services);

        var completed = terminal.Should().BeOfType<WorkflowCompleted>().Subject;
        body.Calls.Should().Be(3, because: "the body runs once per collection item");
        after.Calls.Should().Be(1, because: "the done node runs exactly once, after the last iteration");
        completed.Outputs.Find("after.summary").IfNoneUnsafe((object?)null).Should().Be(
            "loop-finished",
            because: "the done node is an end node, so its output is part of the workflow outputs");
    }

    private object Run(WorkflowDefinition definition, IServiceProvider services)
    {
        var executionId = Guid.NewGuid();
        var parent = this.CreateTestProbe();
        var executor = parent.ChildActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), services));
        executor.Tell(new StartExecution(executionId));
        return parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, RunTimeout);
    }

    private static IServiceProvider Services(params IWorkflowModule[] modules)
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        foreach (var module in modules)
        {
            registry.RegisterModule(module);
        }

        return new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .BuildServiceProvider();
    }

    private static NodeDefinition Node(string id, string moduleId, params (string Key, object? Value)[] properties)
    {
        var props = HashMap<string, JsonElement>.Empty;
        foreach (var (key, value) in properties)
        {
            props = props.AddOrUpdate(key, JsonSerializer.SerializeToElement(value));
        }

        return new NodeDefinition(id, moduleId, id, props);
    }

    private static WorkflowDefinition Definition(
        IEnumerable<NodeDefinition> nodes,
        IEnumerable<ConnectionDefinition> connections)
        => new(
            Id: Guid.NewGuid(),
            Name: "loop-completion-test",
            Description: null,
            Version: new Version(1, 0),
            Nodes: nodes.ToArr(),
            Connections: connections.ToArr(),
            Variables: HashMap<string, VariableDefinition>.Empty);

    /// <summary>Counts its calls and writes a fixed value to one output port.</summary>
    private sealed class CountingModule : IWorkflowModule
    {
        private readonly string outputPort;
        private readonly object? outputValue;
        private int calls;

        public CountingModule(string moduleId, string outputPort, object? outputValue)
        {
            this.ModuleId = moduleId;
            this.outputPort = outputPort;
            this.outputValue = outputValue;
        }

        public int Calls => this.calls;

        public string ModuleId { get; }

        public string DisplayName => this.ModuleId;

        public string Category => "Test";

        public string Description => "Counting test module";

        public string Icon => "🔢";

        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Inputs: new[] { PortDefinition.Create<object>("input", isRequired: false) }.ToArr(),
            Outputs: new[] { PortDefinition.Create<object>(this.outputPort, isRequired: false) }.ToArr(),
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref this.calls);
            return Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { [this.outputPort] = this.outputValue }));
        }
    }
}
