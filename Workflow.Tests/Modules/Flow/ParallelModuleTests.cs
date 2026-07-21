// <copyright file="ParallelModuleTests.cs" company="GlutenFree">
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
/// 🌐 Phase 2.2.3a — Tests for <see cref="ParallelModule"/> (<c>builtin.parallel</c>)~
/// Validates schema, ParallelRequest packaging, branch resolution, and engine integration~ ✨💖
/// </summary>
public sealed class ParallelModuleTests
{
    private readonly ParallelModule _module = new();

    private static ModuleExecutionContext BuildContext(
        Dictionary<string, object?>? properties = null)
        => new()
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = properties ?? new Dictionary<string, object?>(),
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new EmptyServiceProvider(),
            ExecutionId = Guid.NewGuid(),
            NodeId = "parallel-node",
        };

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    [Fact]
    public void ParallelModule_Metadata_IsCorrect()
    {
        _module.ModuleId.Should().Be("builtin.parallel");
        _module.Category.Should().Be("Flow Control");
        _module.DisplayName.Should().Be("Parallel");
        _module.Version.Should().Be(new Version(1, 0, 0));
        _module.Icon.Should().Be("🌐");
    }

    [Fact]
    public void ParallelModule_Schema_HasEmptyOutputs_ForDynamicPorts()
    {
        // Outputs intentionally empty so ValidateConnectionPorts skips port validation~ 🎗️
        _module.Schema.Outputs.Count.Should().Be(0,
            because: "branch ports are dynamic — validation must be skipped~ 🌐");
    }

    [Fact]
    public async Task ExecuteAsync_WithBranchesArray_ReturnsParallelRequestWithCorrectPorts()
    {
        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["branches"] = new List<object?> { "fetch_user", "fetch_orders" },
        });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Parallel.Should().NotBeNull(because: "ParallelModule must return a ParallelRequest~ 🌐");
        result.Parallel!.BranchPorts.Should().Equal("fetch_user", "fetch_orders");
        result.Parallel.DonePort.Should().Be("done");
        result.Parallel.FailFast.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithBranchCount_GeneratesDefaultBranchNames()
    {
        var ctx = BuildContext(new Dictionary<string, object?> { ["branchCount"] = 3 });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Parallel!.BranchPorts.Should().Equal("branch1", "branch2", "branch3");
    }

    [Fact]
    public async Task ExecuteAsync_FailFastOverride_ReflectedInRequest()
    {
        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["branchCount"] = 2,
            ["failFast"] = false,
        });

        var result = await _module.ExecuteAsync(ctx);

        result.Parallel!.FailFast.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_MaxDoPOverride_ReflectedInRequest()
    {
        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["branchCount"] = 5,
            ["maxDegreeOfParallelism"] = 2,
        });

        var result = await _module.ExecuteAsync(ctx);

        result.Parallel!.MaxDegreeOfParallelism.Should().Be(2);
    }

    [Fact]
    public void ValidateConfiguration_DuplicateBranches_Fails()
    {
        var config = new Dictionary<string, object?>
        {
            ["branches"] = new List<object?> { "a", "a", "b" },
        };

        var result = _module.ValidateConfiguration(config);

        result.IsValid.Should().BeFalse(because: "branch port names must be unique~ 💔");
    }

    [Fact]
    public void ValidateConfiguration_ZeroBranchCount_Fails()
    {
        var config = new Dictionary<string, object?> { ["branchCount"] = 0 };

        var result = _module.ValidateConfiguration(config);

        result.IsValid.Should().BeFalse();
    }
}

/// <summary>
/// 🌐 Phase 2.2.3a — Engine integration tests for ParallelModule + ParallelExecutionCoordinator~
/// Validates branch fan-out orchestration through WorkflowExecutor~ ✨💖
/// </summary>
public class ParallelEngineIntegrationTests : TestKit
{
    /// <summary>Counts each invocation; used to verify branch execution counts~ 🔢.</summary>
    private sealed class CountingModule : IWorkflowModule
    {
        private int _count;
        public int Count => _count;

        public string ModuleId => "test.par.counter";
        public string DisplayName => "Counter";
        public string Category => "Test";
        public string Description => "Increments count on each execution~ 🔢";
        public string Icon => "🔢";
        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Inputs: Arr.create(PortDefinition.Create<object>("input", isRequired: false)),
            Outputs: Arr.create(PortDefinition.Create<object>("output", isRequired: false)),
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _count);
            return Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = Count }));
        }
    }

    private sealed class FailingModule : IWorkflowModule
    {
        public string ModuleId => "test.par.failing";
        public string DisplayName => "Failing"; public string Category => "Test";
        public string Description => "Always fails~ ❌"; public string Icon => "❌";
        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Inputs: Arr.create(PortDefinition.Create<object>("input", isRequired: false)),
            Outputs: Arr<PortDefinition>.Empty,
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
            => Task.FromResult(ModuleResult.Fail("Intentional branch failure~ 💔"));
    }

    [Fact]
    public void Parallel_TwoBranches_BothExecute_WorkflowCompletes()
    {
        var counterA = new CountingModule();
        var counterB = new CountingModule();
        var parallelModule = new ParallelModule();

        // Distinct module IDs per branch so we can count independently~
        var branchA = new BranchedModule("test.par.counter.a", counterA);
        var branchB = new BranchedModule("test.par.counter.b", counterB);

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(parallelModule);
        registry.RegisterModule(branchA);
        registry.RegisterModule(branchB);

        var sp = new ServiceCollection().AddSingleton<IModuleRegistry>(registry).BuildServiceProvider();

        var props = new Dictionary<string, JsonElement>
        {
            ["branches"] = JsonSerializer.SerializeToElement(new[] { "branchA", "branchB" }),
        }.ToHashMap();

        var def = new WorkflowDefinition(
            Id: Guid.NewGuid(), Name: "par-2branch", Description: null, Version: new Version(1, 0),
            Nodes: Arr.create(
                new NodeDefinition("par", "builtin.parallel", "Parallel", props),
                new NodeDefinition("a", "test.par.counter.a", "BranchA", HashMap<string, JsonElement>.Empty),
                new NodeDefinition("b", "test.par.counter.b", "BranchB", HashMap<string, JsonElement>.Empty)),
            Connections: Arr.create(
                new ConnectionDefinition("par", "branchA", "a", "input"),
                new ConnectionDefinition("par", "branchB", "b", "input")),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var parent = CreateTestProbe("par-2branch-parent");
        var exec = parent.ChildActorOf(
            WorkflowExecutor.Props(Guid.NewGuid(), def, new Dictionary<string, object?>(), sp),
            "par-2branch-exec");

        exec.Tell(new StartExecution(Guid.NewGuid()));

        var msg = parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(10));
        msg.Should().BeOfType<WorkflowCompleted>(
            because: "two-branch parallel should complete successfully~ 🌐");
        counterA.Count.Should().Be(1, because: "branch A executes exactly once~ 🔢");
        counterB.Count.Should().Be(1, because: "branch B executes exactly once~ 🔢");
    }

    [Fact]
    public void Parallel_DoneBranchFiresAfterAllComplete()
    {
        var counterA = new CountingModule();
        var counterB = new CountingModule();
        var donePost = new CountingModule();
        var parallelModule = new ParallelModule();

        var branchA = new BranchedModule("test.par.counter.a2", counterA);
        var branchB = new BranchedModule("test.par.counter.b2", counterB);
        var post = new BranchedModule("test.par.counter.post", donePost);

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(parallelModule);
        registry.RegisterModule(branchA);
        registry.RegisterModule(branchB);
        registry.RegisterModule(post);

        var sp = new ServiceCollection().AddSingleton<IModuleRegistry>(registry).BuildServiceProvider();

        var props = new Dictionary<string, JsonElement>
        {
            ["branches"] = JsonSerializer.SerializeToElement(new[] { "b1", "b2" }),
        }.ToHashMap();

        var def = new WorkflowDefinition(
            Id: Guid.NewGuid(), Name: "par-done", Description: null, Version: new Version(1, 0),
            Nodes: Arr.create(
                new NodeDefinition("par", "builtin.parallel", "Parallel", props),
                new NodeDefinition("a", "test.par.counter.a2", "A", HashMap<string, JsonElement>.Empty),
                new NodeDefinition("b", "test.par.counter.b2", "B", HashMap<string, JsonElement>.Empty),
                new NodeDefinition("post", "test.par.counter.post", "Post", HashMap<string, JsonElement>.Empty)),
            Connections: Arr.create(
                new ConnectionDefinition("par", "b1", "a", "input"),
                new ConnectionDefinition("par", "b2", "b", "input"),
                new ConnectionDefinition("par", "done", "post", "input")),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var parent = CreateTestProbe("par-done-parent");
        var exec = parent.ChildActorOf(
            WorkflowExecutor.Props(Guid.NewGuid(), def, new Dictionary<string, object?>(), sp),
            "par-done-exec");

        exec.Tell(new StartExecution(Guid.NewGuid()));

        var msg = parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(10));
        msg.Should().BeOfType<WorkflowCompleted>(because: "done port wires post-step in successfully~ ✅");
        counterA.Count.Should().Be(1);
        counterB.Count.Should().Be(1);
        donePost.Count.Should().Be(1, because: "done-port successor must fire exactly once after both branches complete~ ✨");
    }

    [Fact]
    public void Parallel_BranchFails_FailFastTrue_WorkflowFails()
    {
        var failModule = new FailingModule();
        var counter = new CountingModule();
        var parallelModule = new ParallelModule();

        var branchOk = new BranchedModule("test.par.ok", counter);

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(parallelModule);
        registry.RegisterModule(failModule);
        registry.RegisterModule(branchOk);

        var sp = new ServiceCollection().AddSingleton<IModuleRegistry>(registry).BuildServiceProvider();

        var props = new Dictionary<string, JsonElement>
        {
            ["branches"] = JsonSerializer.SerializeToElement(new[] { "ok", "bad" }),
        }.ToHashMap();

        var def = new WorkflowDefinition(
            Id: Guid.NewGuid(), Name: "par-failfast", Description: null, Version: new Version(1, 0),
            Nodes: Arr.create(
                new NodeDefinition("par", "builtin.parallel", "Parallel", props),
                new NodeDefinition("good", "test.par.ok", "Good", HashMap<string, JsonElement>.Empty),
                new NodeDefinition("bad", "test.par.failing", "Bad", HashMap<string, JsonElement>.Empty)),
            Connections: Arr.create(
                new ConnectionDefinition("par", "ok", "good", "input"),
                new ConnectionDefinition("par", "bad", "bad", "input")),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var parent = CreateTestProbe("par-failfast-parent");
        var exec = parent.ChildActorOf(
            WorkflowExecutor.Props(Guid.NewGuid(), def, new Dictionary<string, object?>(), sp),
            "par-failfast-exec");

        exec.Tell(new StartExecution(Guid.NewGuid()));

        var msg = parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(10));
        msg.Should().BeOfType<WorkflowFailed>(
            because: "branch failure with failFast=true should fail the workflow~ ❌");
    }

    /// <summary>
    /// Wraps a CountingModule under a different module ID so the registry can hold multiple
    /// independent counters per workflow~ 🎀.
    /// </summary>
    private sealed class BranchedModule : IWorkflowModule
    {
        private readonly CountingModule _inner;

        public BranchedModule(string id, CountingModule inner)
        {
            ModuleId = id;
            _inner = inner;
        }

        public string ModuleId { get; }
        public string DisplayName => "BranchedCounter";
        public string Category => "Test";
        public string Description => "Aliased counter~";
        public string Icon => "🔢";
        public Version Version => new(1, 0);
        public ModuleSchema Schema => _inner.Schema;

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
            => _inner.ExecuteAsync(ctx, ct);
    }
}


