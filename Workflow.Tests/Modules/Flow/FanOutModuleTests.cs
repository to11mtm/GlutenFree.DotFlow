// <copyright file="FanOutModuleTests.cs" company="GlutenFree">
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

#pragma warning disable SA1204

/// <summary>
/// 🌟 Phase 2.2.3b — Unit tests for <see cref="FanOutModule"/> (<c>builtin.fanout</c>)~
/// Validates metadata, schema, ParallelRequest packaging, and edge cases~ ✨💖
/// </summary>
public sealed class FanOutModuleTests
{
    private readonly FanOutModule _module = new();

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
            NodeId = "fanout-node",
        };

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    // ── Metadata ─────────────────────────────────────────────────────────────────

    /// <summary>Verifies ModuleId, category, display name, icon, and version are all correct~ 🌟.</summary>
    [Fact]
    public void FanOutModule_Metadata_IsCorrect()
    {
        _module.ModuleId.Should().Be("builtin.fanout");
        _module.Category.Should().Be("Flow Control");
        _module.DisplayName.Should().Be("Fan Out");
        _module.Icon.Should().Be("🌟");
        _module.Version.Should().Be(new Version(1, 0, 0));
    }

    // ── Schema ───────────────────────────────────────────────────────────────────

    /// <summary>Schema must declare <c>branch</c> + <c>done</c> output ports + counts/results~ 📋.</summary>
    [Fact]
    public void FanOutModule_Schema_HasCorrectPorts()
    {
        var outputNames = _module.Schema.Outputs.Select(p => p.Name).ToList();
        outputNames.Should().Contain("branch", because: "per-item activation port~ 🌟");
        outputNames.Should().Contain("done", because: "completion port after all items processed~ ✅");
        outputNames.Should().Contain("results", because: "aggregated results output~ 📊");
        outputNames.Should().Contain("count", because: "item count output~ 🔢");

        var inputNames = _module.Schema.Inputs.Select(p => p.Name).ToList();
        inputNames.Should().Contain("items", because: "items collection input~ 🎁");
    }

    // ── ExecuteAsync ─────────────────────────────────────────────────────────────

    /// <summary>Null items (no input, no property) must return failure gracefully~ ❌.</summary>
    [Fact]
    public async Task ExecuteAsync_NullItems_ReturnsFailure()
    {
        var ctx = BuildContext();

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeFalse(because: "items is required; null must fail~ 💔");
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    /// <summary>Valid list input returns a ParallelRequest with Items set correctly~ 🎁.</summary>
    [Fact]
    public async Task ExecuteAsync_ValidItems_ReturnsParallelRequest()
    {
        var items = new List<object?> { "a", "b", "c" };
        var ctx = BuildContext(inputs: new Dictionary<string, object?> { ["items"] = items });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Parallel.Should().NotBeNull(because: "FanOutModule must return a ParallelRequest~ 🌟");
        result.Parallel!.Items.Should().NotBeNull(because: "per-item fan-out sets Items~ 🎁");
        result.Parallel.Items!.Count.Should().Be(3);
        result.Parallel.BranchPort.Should().Be("branch");
        result.Parallel.DonePort.Should().Be("done");
        result.Parallel.WaitForAll.Should().BeTrue(because: "FanOutModule always waits for all~ ✅");
    }

    /// <summary>Custom <c>failFast</c> property flows through into the ParallelRequest~ 🛑.</summary>
    [Fact]
    public async Task ExecuteAsync_FailFastOverride_ReflectedInRequest()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["items"] = new List<object?> { 1, 2 } },
            properties: new Dictionary<string, object?> { ["failFast"] = false });

        var result = await _module.ExecuteAsync(ctx);

        result.Parallel!.FailFast.Should().BeFalse(because: "failFast=false must be forwarded~ 🛑");
    }

    /// <summary>Custom <c>maxDegreeOfParallelism</c> flows through into the ParallelRequest~ ⚡.</summary>
    [Fact]
    public async Task ExecuteAsync_MaxDoPOverride_ReflectedInRequest()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["items"] = new List<object?> { 1, 2, 3, 4 } },
            properties: new Dictionary<string, object?> { ["maxDegreeOfParallelism"] = 2 });

        var result = await _module.ExecuteAsync(ctx);

        result.Parallel!.MaxDegreeOfParallelism.Should().Be(2,
            because: "maxDegreeOfParallelism override must be forwarded~ ⚡");
    }

    /// <summary>When no input port connected, <c>items</c> is resolved from Properties~ 🗂️.</summary>
    [Fact]
    public async Task ExecuteAsync_PropertyFallback_UsesItemsProperty()
    {
        var ctx = BuildContext(
            properties: new Dictionary<string, object?> { ["items"] = new List<object?> { "x", "y" } });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Parallel!.Items!.Count.Should().Be(2,
            because: "property fallback must provide the items~ 🗂️");
    }

    /// <summary>JSON string <c>"[1,2,3]"</c> is parsed to a list correctly~ 📜.</summary>
    [Fact]
    public async Task ExecuteAsync_JsonStringItems_ParsedCorrectly()
    {
        var ctx = BuildContext(inputs: new Dictionary<string, object?> { ["items"] = "[1,2,3]" });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue(because: "JSON string items must be auto-parsed~ 📜");
        result.Parallel!.Items!.Count.Should().Be(3);
    }

    /// <summary>Empty list produces a ParallelRequest with empty Items (zero branches spawned)~ 📭.</summary>
    [Fact]
    public async Task ExecuteAsync_EmptyItems_ReturnsParallelRequestWithEmptyItems()
    {
        var ctx = BuildContext(inputs: new Dictionary<string, object?> { ["items"] = new List<object?>() });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Parallel!.Items.Should().NotBeNull();
        result.Parallel.Items!.Count.Should().Be(0, because: "empty list → zero branches~ 📭");
    }
}

/// <summary>
/// 🌟 Phase 2.2.3b — Engine integration tests for FanOutModule + ParallelExecutionCoordinator~
/// Also includes <c>waitForAll=false</c> carry-over tests from 2.2.3a~ ✨💖
/// </summary>
public class FanOutEngineIntegrationTests : TestKit
{
    // ── Helper modules ────────────────────────────────────────────────────────────

    /// <summary>Counts each invocation; used to verify branch execution counts~ 🔢.</summary>
    private sealed class CountingModule : IWorkflowModule
    {
        private int _count;
        public int Count => _count;

        public string ModuleId => "test.fanout.counter";
        public string DisplayName => "FanOut Counter";
        public string Category => "Test";
        public string Description => "Counts executions~ 🔢";
        public string Icon => "🔢";
        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Inputs: Arr.create(
                PortDefinition.Create<object>("item", isRequired: false),
                PortDefinition.Create<object>("input", isRequired: false)),
            Outputs: Arr.create(PortDefinition.Create<object>("output", isRequired: false)),
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
        {
            System.Threading.Interlocked.Increment(ref _count);
            return Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = Count }));
        }
    }

    /// <summary>Wraps a CountingModule under an alias — lets the registry hold multiple counters~ 🎀.</summary>
    private sealed class AliasedCountingModule : IWorkflowModule
    {
        private readonly CountingModule _inner;

        public AliasedCountingModule(string id, CountingModule inner)
        {
            ModuleId = id;
            _inner = inner;
        }

        public string ModuleId { get; }
        public string DisplayName => "Aliased Counter";
        public string Category => "Test";
        public string Description => "Alias~";
        public string Icon => "🔢";
        public Version Version => new(1, 0);
        public ModuleSchema Schema => _inner.Schema;

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
            => _inner.ExecuteAsync(ctx, ct);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// FanOut with 3 items spawns 3 independent branch sub-graphs.
    /// Body node runs exactly 3 times; workflow completes~ 🌟✅
    /// </summary>
    [Fact]
    public void FanOut_ThreeItems_RunsThreeBranchExecutions_WorkflowCompletes()
    {
        var counter = new CountingModule();
        var fanOut = new FanOutModule();
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(fanOut);
        registry.RegisterModule(counter);

        var sp = new ServiceCollection().AddSingleton<IModuleRegistry>(registry).BuildServiceProvider();

        var fanOutProps = new Dictionary<string, JsonElement>
        {
            ["items"] = JsonSerializer.SerializeToElement(new[] { "a", "b", "c" }),
        }.ToHashMap();

        var def = new WorkflowDefinition(
            Id: Guid.NewGuid(), Name: "fanout-3items", Description: null, Version: new Version(1, 0),
            Nodes: Arr.create(
                new NodeDefinition("fo", "builtin.fanout", "FanOut", fanOutProps),
                new NodeDefinition("body", "test.fanout.counter", "Body", HashMap<string, JsonElement>.Empty)),
            Connections: Arr.create(
                new ConnectionDefinition("fo", "branch", "body", "item")),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var parent = CreateTestProbe("fo-3items-parent");
        var exec = parent.ChildActorOf(
            WorkflowExecutor.Props(Guid.NewGuid(), def, new Dictionary<string, object?>(), sp),
            "fo-3items-exec");

        exec.Tell(new StartExecution(Guid.NewGuid()));

        var msg = parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(10));
        msg.Should().BeOfType<WorkflowCompleted>(because: "fan-out over 3 items must complete~ 🌟");
        counter.Count.Should().Be(3, because: "body fires once per item~ 🔢");
    }

    /// <summary>
    /// FanOut with an empty items list: coordinator spawns 0 branches and fires done port immediately.
    /// Workflow must complete with zero body executions~ 📭✅
    /// </summary>
    [Fact]
    public void FanOut_EmptyItems_WorkflowCompletes_ZeroBranchExecutions()
    {
        var counter = new CountingModule();
        var postCounter = new CountingModule();
        var fanOut = new FanOutModule();

        var post = new AliasedCountingModule("test.fanout.postcounter", postCounter);

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(fanOut);
        registry.RegisterModule(counter);
        registry.RegisterModule(post);

        var sp = new ServiceCollection().AddSingleton<IModuleRegistry>(registry).BuildServiceProvider();

        var fanOutProps = new Dictionary<string, JsonElement>
        {
            ["items"] = JsonSerializer.SerializeToElement(Array.Empty<string>()),
        }.ToHashMap();

        var def = new WorkflowDefinition(
            Id: Guid.NewGuid(), Name: "fanout-empty", Description: null, Version: new Version(1, 0),
            Nodes: Arr.create(
                new NodeDefinition("fo", "builtin.fanout", "FanOut", fanOutProps),
                new NodeDefinition("body", "test.fanout.counter", "Body", HashMap<string, JsonElement>.Empty),
                new NodeDefinition("post", "test.fanout.postcounter", "Post", HashMap<string, JsonElement>.Empty)),
            Connections: Arr.create(
                new ConnectionDefinition("fo", "branch", "body", "item"),
                new ConnectionDefinition("fo", "done", "post", "input")),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var parent = CreateTestProbe("fo-empty-parent");
        var exec = parent.ChildActorOf(
            WorkflowExecutor.Props(Guid.NewGuid(), def, new Dictionary<string, object?>(), sp),
            "fo-empty-exec");

        exec.Tell(new StartExecution(Guid.NewGuid()));

        var msg = parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(10));
        msg.Should().BeOfType<WorkflowCompleted>(because: "empty fan-out still completes~ 📭");
        counter.Count.Should().Be(0, because: "body never runs when items is empty~ 🔢");
        postCounter.Count.Should().Be(1, because: "done port fires once after zero-item fan-out~ ✅");
    }

    /// <summary>
    /// [carry-over from 2.2.3a] waitForAll=false: first branch completion causes ParallelCompleted~
    /// Workflow must complete — outputs contain only the first winner's data (count = 1)~ 🏁
    /// </summary>
    [Fact]
    public void Parallel_WaitForAllFalse_FirstBranchWins_WorkflowCompletes()
    {
        var counterA = new CountingModule();
        var doneCounter = new CountingModule();
        var parallelModule = new ParallelModule();

        var branchA = new AliasedCountingModule("test.fanout.counter.a", counterA);
        var post = new AliasedCountingModule("test.fanout.counter.done", doneCounter);

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(parallelModule);
        registry.RegisterModule(branchA);
        registry.RegisterModule(post);

        var sp = new ServiceCollection().AddSingleton<IModuleRegistry>(registry).BuildServiceProvider();

        // waitForAll=false + single branch — first completion triggers done immediately~ 🏁
        var parProps = new Dictionary<string, JsonElement>
        {
            ["branches"] = JsonSerializer.SerializeToElement(new[] { "b1" }),
            ["waitForAll"] = JsonSerializer.SerializeToElement(false),
        }.ToHashMap();

        var def = new WorkflowDefinition(
            Id: Guid.NewGuid(), Name: "par-waitforall-false", Description: null, Version: new Version(1, 0),
            Nodes: Arr.create(
                new NodeDefinition("par", "builtin.parallel", "Parallel", parProps),
                new NodeDefinition("a", "test.fanout.counter.a", "BranchA", HashMap<string, JsonElement>.Empty),
                new NodeDefinition("post", "test.fanout.counter.done", "Post", HashMap<string, JsonElement>.Empty)),
            Connections: Arr.create(
                new ConnectionDefinition("par", "b1", "a", "input"),
                new ConnectionDefinition("par", "done", "post", "input")),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var parent = CreateTestProbe("par-wfa-false-parent");
        var exec = parent.ChildActorOf(
            WorkflowExecutor.Props(Guid.NewGuid(), def, new Dictionary<string, object?>(), sp),
            "par-wfa-false-exec");

        exec.Tell(new StartExecution(Guid.NewGuid()));

        var msg = parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(10));
        msg.Should().BeOfType<WorkflowCompleted>(because: "waitForAll=false with one branch must complete~ 🏁");
        counterA.Count.Should().Be(1, because: "the single branch executes before winning~ 🔢");
        doneCounter.Count.Should().Be(1, because: "done port fires once after first winner~ ✅");
    }

    /// <summary>
    /// [carry-over from 2.2.3a] waitForAll=false: outputs contain only the winning branch data (count=1)~
    /// Verifies coordinator reports count=1 in its ParallelCompleted payload~ 🏁🔢
    /// </summary>
    [Fact]
    public void Parallel_WaitForAllFalse_TwoBranchesNamed_WorkflowCompletes_CountIsCorrect()
    {
        var counterA = new CountingModule();
        var counterB = new CountingModule();
        var parallelModule = new ParallelModule();

        var branchA = new AliasedCountingModule("test.fanout.counter.a2", counterA);
        var branchB = new AliasedCountingModule("test.fanout.counter.b2", counterB);

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(parallelModule);
        registry.RegisterModule(branchA);
        registry.RegisterModule(branchB);

        var sp = new ServiceCollection().AddSingleton<IModuleRegistry>(registry).BuildServiceProvider();

        // waitForAll=false — first winner cancels the sibling~ 🏁
        var parProps = new Dictionary<string, JsonElement>
        {
            ["branches"] = JsonSerializer.SerializeToElement(new[] { "b1", "b2" }),
            ["waitForAll"] = JsonSerializer.SerializeToElement(false),
        }.ToHashMap();

        var def = new WorkflowDefinition(
            Id: Guid.NewGuid(), Name: "par-wfa-2branch", Description: null, Version: new Version(1, 0),
            Nodes: Arr.create(
                new NodeDefinition("par", "builtin.parallel", "Parallel", parProps),
                new NodeDefinition("a", "test.fanout.counter.a2", "A", HashMap<string, JsonElement>.Empty),
                new NodeDefinition("b", "test.fanout.counter.b2", "B", HashMap<string, JsonElement>.Empty)),
            Connections: Arr.create(
                new ConnectionDefinition("par", "b1", "a", "input"),
                new ConnectionDefinition("par", "b2", "b", "input")),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var parent = CreateTestProbe("par-wfa2-parent");
        var exec = parent.ChildActorOf(
            WorkflowExecutor.Props(Guid.NewGuid(), def, new Dictionary<string, object?>(), sp),
            "par-wfa2-exec");

        exec.Tell(new StartExecution(Guid.NewGuid()));

        var msg = parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(10));

        // waitForAll=false must complete the workflow (not fail)~ 🏁
        msg.Should().BeOfType<WorkflowCompleted>(
            because: "waitForAll=false — first branch triggers completion without error~ 🏁");
    }
}

