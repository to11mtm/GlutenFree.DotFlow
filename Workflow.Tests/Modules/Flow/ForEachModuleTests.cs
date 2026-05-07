// <copyright file="ForEachModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Flow;

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
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Flow;
using Xunit;

/// <summary>
/// 🔁 Phase 2.2.2 — Tests for <see cref="ForEachModule"/> (<c>builtin.loop.foreach</c>)~
/// Validates schema, LoopRequest packaging, collection coercion, and engine integration~ ✨💖
/// </summary>
public sealed class ForEachModuleTests
{
    private readonly ForEachModule _module = new();

    private static ModuleExecutionContext BuildContext(
        Dictionary<string, object?>? inputs = null,
        Dictionary<string, object?>? properties = null)
        => new()
        {
            Inputs = inputs ?? new Dictionary<string, object?>(),
            Properties = properties ?? new Dictionary<string, object?>(),
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new EmptyServiceProvider(),
            ExecutionId = Guid.NewGuid(),
            NodeId = "foreach-node",
        };

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    // ── Schema & identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void ForEachModule_Metadata_IsCorrect()
    {
        _module.ModuleId.Should().Be("builtin.loop.foreach");
        _module.Category.Should().Be("Flow Control");
        _module.DisplayName.Should().Be("For Each");
        _module.Version.Should().Be(new Version(1, 0, 0));
        _module.Icon.Should().Be("🔁");
    }

    [Fact]
    public void ForEachModule_Schema_DeclaresPorts()
    {
        _module.Schema.Inputs.ToList().Select(p => p.Name).Should().Contain("collection");
        _module.Schema.Outputs.ToList().Select(p => p.Name).Should()
            .Contain("loopBody").And.Contain("done").And.Contain("results").And.Contain("count");
    }

    // ── LoopRequest packaging ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WithList_ReturnsLoopRequest()
    {
        var items = new List<object?> { "a", "b", "c" };
        var ctx = BuildContext(inputs: new Dictionary<string, object?> { ["collection"] = items });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Loop.Should().NotBeNull(because: "ForEachModule must return a LoopRequest~ 🔁");
        result.Loop!.Items.Should().HaveCount(3);
        result.Loop.LoopBodyPort.Should().Be("loopBody");
        result.Loop.DonePort.Should().Be("done");
    }

    [Fact]
    public async Task ExecuteAsync_MaxIterationsOverride_ReflectedInLoopRequest()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["collection"] = new List<object?> { 1, 2 } },
            properties: new Dictionary<string, object?> { ["maxIterations"] = 50 });

        var result = await _module.ExecuteAsync(ctx);

        result.Loop!.MaxIterations.Should().Be(50, because: "property maxIterations should override default 1000~ 🔢");
    }

    [Fact]
    public async Task ExecuteAsync_ContinueOnErrorTrue_ReflectedInLoopRequest()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["collection"] = new List<object?> { 1 } },
            properties: new Dictionary<string, object?> { ["continueOnError"] = true });

        var result = await _module.ExecuteAsync(ctx);

        result.Loop!.ContinueOnError.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_EmptyCollection_ReturnsLoopRequestWithZeroItems()
    {
        var ctx = BuildContext(inputs: new Dictionary<string, object?> { ["collection"] = new List<object?>() });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Loop.Should().NotBeNull();
        result.Loop!.Items.Should().BeEmpty(because: "empty collection = 0 iterations~ 🔁");
    }

    [Fact]
    public async Task ExecuteAsync_NullCollection_ReturnsFail()
    {
        var ctx = BuildContext(inputs: new Dictionary<string, object?>());

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeFalse(because: "collection is required~ ❌");
        result.ErrorMessage.Should().Contain("collection");
    }

    [Fact]
    public async Task ExecuteAsync_PropertyCollection_UsedWhenNoInputPort()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?>(),
            properties: new Dictionary<string, object?> { ["collection"] = new List<object?> { "x", "y" } });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Loop!.Items.Should().HaveCount(2,
            because: "property 'collection' fallback should work~ 💬");
    }

    [Fact]
    public async Task ExecuteAsync_JsonStringCollection_ParsedCorrectly()
    {
        var json = """["alpha","beta","gamma"]""";
        var ctx = BuildContext(inputs: new Dictionary<string, object?> { ["collection"] = json });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Loop!.Items.Should().HaveCount(3,
            because: "JSON string array should be parsed~ 📋");
    }
}

/// <summary>
/// 🔁 Phase 2.2.2 — Engine integration tests for ForEachModule + LoopExecutorActor~
/// Validates real iteration orchestration through WorkflowExecutor~ ✨💖
/// </summary>
public class ForEachEngineIntegrationTests : TestKit
{
    private static IServiceProvider BuildServiceProvider(params IWorkflowModule[] modules)
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        foreach (var m in modules) registry.RegisterModule(m);

        return new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .BuildServiceProvider();
    }

    private sealed class CountingModule : IWorkflowModule
    {
        private int _count;
        public int Count => _count;

        public string ModuleId => "test.counter";
        public string DisplayName => "Counter";
        public string Category => "Test";
        public string Description => "Increments count on each execution~ 🔢";
        public string Icon => "🔢";
        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Inputs: Arr.create(PortDefinition.Create<object>("item", isRequired: false)),
            Outputs: Arr.create(PortDefinition.Create<object>("output", isRequired: false)),
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
        {
            System.Threading.Interlocked.Increment(ref _count);
            return Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = Count }));
        }
    }

    private sealed class FailingModule : IWorkflowModule
    {
        public string ModuleId => "test.failing";
        public string DisplayName => "Failing"; public string Category => "Test";
        public string Description => "Always fails~ ❌"; public string Icon => "❌";
        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Inputs: Arr<PortDefinition>.Empty,
            Outputs: Arr<PortDefinition>.Empty,
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
            => Task.FromResult(ModuleResult.Fail("Intentional failure~ 💔"));
    }

    // ── Integration tests ────────────────────────────────────────────────────────────

    [Fact]
    public void ForEach_ThreeItems_RunsThreeIterations_WorkflowCompletes()
    {
        // Arrange
        var counter = new CountingModule();
        var foreachModule = new ForEachModule();

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(foreachModule);
        registry.RegisterModule(counter);

        var sp = new ServiceCollection().AddSingleton<IModuleRegistry>(registry).BuildServiceProvider();

        var props = new Dictionary<string, JsonElement>
        {
            ["collection"] = JsonSerializer.SerializeToElement(new[] { "a", "b", "c" }),
        }.ToHashMap();

        var def = new WorkflowDefinition(
            Id: Guid.NewGuid(), Name: "foreach-3items", Description: null, Version: new Version(1, 0),
            Nodes: Arr.create(
                new NodeDefinition("fe", "builtin.loop.foreach", "ForEach", props),
                new NodeDefinition("body", "test.counter", "Body", HashMap<string, JsonElement>.Empty)),
            Connections: Arr.create(
                new ConnectionDefinition("fe", "loopBody", "body", "item")),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var parent = CreateTestProbe("foreach-parent");
        var exec = parent.ChildActorOf(
            WorkflowExecutor.Props(Guid.NewGuid(), def, new Dictionary<string, object?>(), sp),
            "foreach-exec");

        // Act
        exec.Tell(new StartExecution(Guid.NewGuid()));

        // Assert
        var msg = parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(10));
        msg.Should().BeOfType<WorkflowCompleted>(because: "foreach over 3 items should complete successfully~ 🔁");
        counter.Count.Should().Be(3, because: "body node should run once per item~ 🔢");
    }

    [Fact]
    public void ForEach_EmptyCollection_WorkflowCompletes_ZeroBodyExecutions()
    {
        // Arrange
        var counter = new CountingModule();
        var foreachModule = new ForEachModule();

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(foreachModule);
        registry.RegisterModule(counter);

        var sp = new ServiceCollection().AddSingleton<IModuleRegistry>(registry).BuildServiceProvider();

        var props = new Dictionary<string, JsonElement>
        {
            ["collection"] = JsonSerializer.SerializeToElement(Array.Empty<string>()),
        }.ToHashMap();

        var def = new WorkflowDefinition(
            Id: Guid.NewGuid(), Name: "foreach-empty", Description: null, Version: new Version(1, 0),
            Nodes: Arr.create(
                new NodeDefinition("fe", "builtin.loop.foreach", "ForEach", props),
                new NodeDefinition("body", "test.counter", "Body", HashMap<string, JsonElement>.Empty)),
            Connections: Arr.create(
                new ConnectionDefinition("fe", "loopBody", "body", "item")),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var parent = CreateTestProbe("foreach-empty-parent");
        var exec = parent.ChildActorOf(
            WorkflowExecutor.Props(Guid.NewGuid(), def, new Dictionary<string, object?>(), sp),
            "foreach-empty-exec");

        exec.Tell(new StartExecution(Guid.NewGuid()));

        var msg = parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(10));
        msg.Should().BeOfType<WorkflowCompleted>(because: "empty collection loop should still complete~ ✅");
        counter.Count.Should().Be(0, because: "body should not run for empty collection~ 🔢");
    }

    [Fact]
    public void ForEach_BodyFails_ContinueOnErrorFalse_WorkflowFails()
    {
        var failModule = new FailingModule();
        var foreachModule = new ForEachModule();

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(foreachModule);
        registry.RegisterModule(failModule);

        var sp = new ServiceCollection().AddSingleton<IModuleRegistry>(registry).BuildServiceProvider();

        var props = new Dictionary<string, JsonElement>
        {
            ["collection"] = JsonSerializer.SerializeToElement(new[] { 1, 2 }),
        }.ToHashMap();

        var def = new WorkflowDefinition(
            Id: Guid.NewGuid(), Name: "foreach-fail", Description: null, Version: new Version(1, 0),
            Nodes: Arr.create(
                new NodeDefinition("fe", "builtin.loop.foreach", "ForEach", props),
                new NodeDefinition("body", "test.failing", "Body", HashMap<string, JsonElement>.Empty)),
            Connections: Arr.create(
                new ConnectionDefinition("fe", "loopBody", "body", "input")),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var parent = CreateTestProbe("foreach-fail-parent");
        var exec = parent.ChildActorOf(
            WorkflowExecutor.Props(Guid.NewGuid(), def, new Dictionary<string, object?>(), sp),
            "foreach-fail-exec");

        exec.Tell(new StartExecution(Guid.NewGuid()));

        var msg = parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(10));
        msg.Should().BeOfType<WorkflowFailed>(
            because: "body failure with continueOnError=false should fail the workflow~ ❌");
    }

    [Fact]
    public void ForEach_BreakModule_StopsEarly()
    {
        var counter = new CountingModule();
        var breakMod = new BreakModule();
        var foreachModule = new ForEachModule();

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(foreachModule);
        registry.RegisterModule(counter);
        registry.RegisterModule(breakMod);

        var sp = new ServiceCollection().AddSingleton<IModuleRegistry>(registry).BuildServiceProvider();

        var props = new Dictionary<string, JsonElement>
        {
            ["collection"] = JsonSerializer.SerializeToElement(new[] { 1, 2, 3, 4, 5 }),
        }.ToHashMap();

        var def = new WorkflowDefinition(
            Id: Guid.NewGuid(), Name: "foreach-break", Description: null, Version: new Version(1, 0),
            Nodes: Arr.create(
                new NodeDefinition("fe", "builtin.loop.foreach", "ForEach", props),
                new NodeDefinition("counter", "test.counter", "Counter", HashMap<string, JsonElement>.Empty),
                new NodeDefinition("brk", "builtin.break", "Break", HashMap<string, JsonElement>.Empty)),
            Connections: Arr.create(
                new ConnectionDefinition("fe", "loopBody", "counter", "item"),
                new ConnectionDefinition("counter", "output", "brk", "input")),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var parent = CreateTestProbe("foreach-break-parent");
        var exec = parent.ChildActorOf(
            WorkflowExecutor.Props(Guid.NewGuid(), def, new Dictionary<string, object?>(), sp),
            "foreach-break-exec");

        exec.Tell(new StartExecution(Guid.NewGuid()));

        var msg = parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(10));
        msg.Should().BeOfType<WorkflowCompleted>(
            because: "break should stop the loop at iteration 1 and complete workflow~ ⏹️");
        counter.Count.Should().Be(1,
            because: "break in first iteration = exactly 1 body execution~ ⏹️");
    }
}


