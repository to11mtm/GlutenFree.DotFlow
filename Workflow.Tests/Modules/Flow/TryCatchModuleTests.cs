// <copyright file="TryCatchModuleTests.cs" company="GlutenFree">
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
/// 🛡️ Phase 2.2.4 — Tests for <see cref="TryCatchModule"/> (<c>builtin.trycatch</c>),
/// <see cref="ThrowModule"/> (<c>builtin.throw</c>), and <see cref="WorkflowError"/>~
/// Covers module unit tests, engine integration, and error routing semantics~ ✨💖
/// </summary>
public sealed class TryCatchModuleTests : TestKit
{
    // ── Unit: TryCatchModule ────────────────────────────────────────────────────────────

    private readonly TryCatchModule _module = new();

    private static ModuleExecutionContext BuildTryCatchContext(
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
            NodeId = "trycatch-node",
        };

    [Fact]
    public void TryCatchModule_Metadata_IsCorrect()
    {
        _module.ModuleId.Should().Be("builtin.trycatch");
        _module.Category.Should().Be("Flow Control");
        _module.DisplayName.Should().Be("Try Catch");
        _module.Version.Should().Be(new Version(1, 0, 0));
        _module.Icon.Should().Be("🛡️");
    }

    [Fact]
    public void TryCatchModule_Schema_HasEmptyOutputs_ForDynamicPorts()
    {
        _module.Schema.Outputs.Count.Should().Be(0,
            because: "dynamic ports (try/catch/finally/done) skip ValidateConnectionPorts~ 🎗️");
        _module.Schema.Inputs.Count.Should().BeGreaterThan(0,
            because: "rethrow and catchTypes are declared inputs~ 💖");
    }

    [Fact]
    public async Task TryCatchModule_ExecuteAsync_ReturnsTryCatchRequest()
    {
        var ctx = BuildTryCatchContext();
        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.TryCatch.Should().NotBeNull("module must return a TryCatchRequest for the engine~ 🛡️");
        result.TryCatch!.TryPort.Should().Be("try");
        result.TryCatch.CatchPort.Should().Be("catch");
        result.TryCatch.FinallyPort.Should().Be("finally");
        result.TryCatch.DonePort.Should().Be("done");
    }

    [Fact]
    public async Task TryCatchModule_Rethrow_True_PassedThrough()
    {
        var ctx = BuildTryCatchContext(properties: new Dictionary<string, object?> { ["rethrow"] = true });
        var result = await _module.ExecuteAsync(ctx);

        result.TryCatch!.Rethrow.Should().BeTrue(
            because: "rethrow=true should be forwarded to the TryCatchRequest~ ❗");
    }

    [Fact]
    public async Task TryCatchModule_Rethrow_DefaultsFalse()
    {
        var ctx = BuildTryCatchContext();
        var result = await _module.ExecuteAsync(ctx);

        result.TryCatch!.Rethrow.Should().BeFalse(
            because: "rethrow defaults to false when not provided~ 💖");
    }

    [Fact]
    public async Task TryCatchModule_CatchTypes_ParsedFromCommaSeparatedString()
    {
        var ctx = BuildTryCatchContext(
            properties: new Dictionary<string, object?> { ["catchTypes"] = "ValidationError,TimeoutError" });
        var result = await _module.ExecuteAsync(ctx);

        result.TryCatch!.CatchTypes.Should().BeEquivalentTo(
            new[] { "ValidationError", "TimeoutError" },
            because: "comma-separated string should parse into string[]~ 🎣");
    }

    [Fact]
    public async Task TryCatchModule_CatchTypes_NullWhenEmpty()
    {
        var ctx = BuildTryCatchContext();
        var result = await _module.ExecuteAsync(ctx);

        result.TryCatch!.CatchTypes.Should().BeNull(
            because: "no catchTypes = catch-all boundary~ 🎣");
    }

    // ── Unit: ThrowModule ────────────────────────────────────────────────────────────────

    private readonly ThrowModule _throwModule = new();

    private static ModuleExecutionContext BuildThrowContext(
        Dictionary<string, object?>? properties = null, Dictionary<string, object?>? inputs = null)
        => new()
        {
            Inputs = inputs ?? new Dictionary<string, object?>(),
            Properties = properties ?? new Dictionary<string, object?>(),
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new EmptyServiceProvider(),
            ExecutionId = Guid.NewGuid(),
            NodeId = "throw-node",
        };

    [Fact]
    public void ThrowModule_Metadata_IsCorrect()
    {
        _throwModule.ModuleId.Should().Be("builtin.throw");
        _throwModule.Category.Should().Be("Flow Control");
        _throwModule.DisplayName.Should().Be("Throw Error");
        _throwModule.Version.Should().Be(new Version(1, 0, 0));
        _throwModule.Icon.Should().Be("💥");
    }

    [Fact]
    public async Task ThrowModule_ExecuteAsync_ThrowsWorkflowUserException()
    {
        var ctx = BuildThrowContext(properties: new Dictionary<string, object?>
        {
            ["errorType"] = "ValidationError",
            ["message"] = "Input is invalid",
        });

        var act = () => _throwModule.ExecuteAsync(ctx);
        await act.Should().ThrowAsync<WorkflowUserException>(
            because: "ThrowModule must always throw, never succeed~ 💥");
    }

    [Fact]
    public async Task ThrowModule_ExecuteAsync_ErrorTypeAndMessageFromProperties()
    {
        var ctx = BuildThrowContext(properties: new Dictionary<string, object?>
        {
            ["errorType"] = "CustomError",
            ["message"] = "Something went wrong",
        });

        var ex = await Assert.ThrowsAsync<WorkflowUserException>(
            () => _throwModule.ExecuteAsync(ctx));

        ex.ErrorType.Should().Be("CustomError", "errorType from properties");
        ex.Message.Should().Be("Something went wrong", "message from properties");
    }

    [Fact]
    public async Task ThrowModule_ExecuteAsync_DataPayloadAttached()
    {
        var ctx = BuildThrowContext(
            inputs: new Dictionary<string, object?>
            {
                ["errorType"] = "DataError",
                ["message"] = "Data problem",
                ["data"] = new Dictionary<string, object?> { ["detail"] = "row 5" },
            });

        var ex = await Assert.ThrowsAsync<WorkflowUserException>(
            () => _throwModule.ExecuteAsync(ctx));

        ex.Data.Should().NotBeNull("data payload should be attached~ 📦");
    }

    // ── Unit: WorkflowError ──────────────────────────────────────────────────────────────

    [Fact]
    public void WorkflowError_FromException_PopulatesFields()
    {
        var ex = new InvalidOperationException("something broke");
        var error = WorkflowError.FromException(ex, nodeId: "node1");

        error.ErrorType.Should().Be("InvalidOperationException");
        error.Message.Should().Be("something broke");
        error.NodeId.Should().Be("node1");
        // CopilotNote: DateTimeOffset.Should() is ambiguous — check via a guard instead~
        (DateTimeOffset.UtcNow - error.OccurredAt).Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void WorkflowError_FromException_ExtractsWorkflowUserExceptionType()
    {
        var ex = new WorkflowUserException("ValidationError", "Invalid input", new { field = "name" });
        var error = WorkflowError.FromException(ex, nodeId: "node2");

        error.ErrorType.Should().Be("ValidationError",
            because: "WorkflowUserException.ErrorType should be used, not the CLR type name~ 🏷️");
        error.Message.Should().Be("Invalid input");
        error.NodeId.Should().Be("node2");
        error.Data.Should().NotBeNull("data payload should be preserved~ 📦");
    }

    // ── Engine Integration Tests ──────────────────────────────────────────────────────────

    /// <summary>
    /// TryCatch with a passing try body + finally:
    /// only try and finally should run; workflow should complete with success=true~ ✅🧹
    /// </summary>
    [Fact]
    public void TryCatch_TrySucceeds_Finally_AlwaysRuns_WorkflowCompletes()
    {
        // Workflow:  tc → (try) → tryBody
        //           tc → (finally) → finallyBody
        //           tc → (done) → postNode
        var tryCounter = new CountingModule();
        var finallyCounter = new CountingModule();
        var postCounter = new CountingModule();

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new TryCatchModule());
        registry.RegisterModule(new BranchedModule("mod.trybody", tryCounter));
        registry.RegisterModule(new BranchedModule("mod.finally", finallyCounter));
        registry.RegisterModule(new BranchedModule("mod.post", postCounter));

        var sp = new ServiceCollection().AddSingleton<IModuleRegistry>(registry).BuildServiceProvider();

        var def = new WorkflowDefinition(
            Id: Guid.NewGuid(), Name: "tc-try-success", Description: null, Version: new Version(1, 0),
            Nodes: Arr.create(
                new NodeDefinition("tc", "builtin.trycatch", "TryCatch", HashMap<string, JsonElement>.Empty),
                new NodeDefinition("tryBody", "mod.trybody", "TryBody", HashMap<string, JsonElement>.Empty),
                new NodeDefinition("finallyBody", "mod.finally", "Finally", HashMap<string, JsonElement>.Empty),
                new NodeDefinition("postNode", "mod.post", "Post", HashMap<string, JsonElement>.Empty)),
            Connections: Arr.create(
                new ConnectionDefinition("tc", "try", "tryBody", "input"),
                new ConnectionDefinition("tc", "finally", "finallyBody", "input"),
                new ConnectionDefinition("tc", "done", "postNode", "input")),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var parent = CreateTestProbe("tc-try-success-parent");
        var exec = parent.ChildActorOf(
            WorkflowExecutor.Props(Guid.NewGuid(), def, new Dictionary<string, object?>(), sp),
            "tc-try-success-exec");

        exec.Tell(new StartExecution(Guid.NewGuid()));

        var msg = parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(10));
        msg.Should().BeOfType<WorkflowCompleted>(
            because: "try succeeds → finally runs → done fires → workflow completes~ ✅");
        tryCounter.Count.Should().Be(1, "try body runs once on success");
        finallyCounter.Count.Should().Be(1, "finally always runs after try succeeds~ 🧹");
        postCounter.Count.Should().Be(1, "post-done node fires after sequence completes");
    }

    /// <summary>
    /// TryCatch with a failing try body: routes to catch, workflow completes (not fails)~ 🛡️
    /// </summary>
    [Fact]
    public void TryCatch_TryFails_RoutesToCatch_WorkflowCompletes()
    {
        var catchCounter = new CountingModule();

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new TryCatchModule());
        registry.RegisterModule(new ThrowModule());
        registry.RegisterModule(new BranchedModule("mod.catch", catchCounter));

        var sp = new ServiceCollection().AddSingleton<IModuleRegistry>(registry).BuildServiceProvider();

        var throwProps = new Dictionary<string, JsonElement>
        {
            ["errorType"] = JsonSerializer.SerializeToElement("TestError"),
            ["message"] = JsonSerializer.SerializeToElement("Test failure"),
        }.ToHashMap();

        var def = new WorkflowDefinition(
            Id: Guid.NewGuid(), Name: "tc-catch", Description: null, Version: new Version(1, 0),
            Nodes: Arr.create(
                new NodeDefinition("tc", "builtin.trycatch", "TryCatch", HashMap<string, JsonElement>.Empty),
                new NodeDefinition("throwNode", "builtin.throw", "Throw", throwProps),
                new NodeDefinition("catchNode", "mod.catch", "Catch", HashMap<string, JsonElement>.Empty)),
            Connections: Arr.create(
                new ConnectionDefinition("tc", "try", "throwNode", "input"),
                new ConnectionDefinition("tc", "catch", "catchNode", "input")),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var parent = CreateTestProbe("tc-catch-parent");
        var exec = parent.ChildActorOf(
            WorkflowExecutor.Props(Guid.NewGuid(), def, new Dictionary<string, object?>(), sp),
            "tc-catch-exec");

        exec.Tell(new StartExecution(Guid.NewGuid()));

        var msg = parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(10));
        msg.Should().BeOfType<WorkflowCompleted>(
            because: "error boundary catches the failure — workflow should complete, not fail~ 🛡️");
        catchCounter.Count.Should().Be(1, "catch handler must run exactly once when try fails~ 🪤");
    }

    /// <summary>
    /// TryCatch with failing try + finally: both catch and finally run; success outcome~ 🛡️🧹
    /// </summary>
    [Fact]
    public void TryCatch_TryFails_CatchAndFinally_BothRun_WorkflowCompletes()
    {
        var catchCounter = new CountingModule();
        var finallyCounter = new CountingModule();

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new TryCatchModule());
        registry.RegisterModule(new ThrowModule());
        registry.RegisterModule(new BranchedModule("mod.catch", catchCounter));
        registry.RegisterModule(new BranchedModule("mod.finally", finallyCounter));

        var sp = new ServiceCollection().AddSingleton<IModuleRegistry>(registry).BuildServiceProvider();

        var throwProps = new Dictionary<string, JsonElement>
        {
            ["errorType"] = JsonSerializer.SerializeToElement("TestError"),
            ["message"] = JsonSerializer.SerializeToElement("Test failure"),
        }.ToHashMap();

        var def = new WorkflowDefinition(
            Id: Guid.NewGuid(), Name: "tc-catch-finally", Description: null, Version: new Version(1, 0),
            Nodes: Arr.create(
                new NodeDefinition("tc", "builtin.trycatch", "TryCatch", HashMap<string, JsonElement>.Empty),
                new NodeDefinition("throwNode", "builtin.throw", "Throw", throwProps),
                new NodeDefinition("catchNode", "mod.catch", "Catch", HashMap<string, JsonElement>.Empty),
                new NodeDefinition("finallyNode", "mod.finally", "Finally", HashMap<string, JsonElement>.Empty)),
            Connections: Arr.create(
                new ConnectionDefinition("tc", "try", "throwNode", "input"),
                new ConnectionDefinition("tc", "catch", "catchNode", "input"),
                new ConnectionDefinition("tc", "finally", "finallyNode", "input")),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var parent = CreateTestProbe("tc-catch-finally-parent");
        var exec = parent.ChildActorOf(
            WorkflowExecutor.Props(Guid.NewGuid(), def, new Dictionary<string, object?>(), sp),
            "tc-catch-finally-exec");

        exec.Tell(new StartExecution(Guid.NewGuid()));

        var msg = parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(10));
        msg.Should().BeOfType<WorkflowCompleted>("catch + finally both handled gracefully~ ✅");
        catchCounter.Count.Should().Be(1, "catch runs when try fails~ 🪤");
        finallyCounter.Count.Should().Be(1, "finally always runs even when try fails~ 🧹");
    }

    /// <summary>
    /// TryCatch with rethrow=true: finally runs, then workflow fails with original error~ ❗
    /// </summary>
    [Fact]
    public void TryCatch_Rethrow_True_WorkflowFails_AfterFinally()
    {
        var finallyCounter = new CountingModule();

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new TryCatchModule());
        registry.RegisterModule(new ThrowModule());
        registry.RegisterModule(new BranchedModule("mod.finally", finallyCounter));

        var sp = new ServiceCollection().AddSingleton<IModuleRegistry>(registry).BuildServiceProvider();

        var tcProps = new Dictionary<string, JsonElement>
        {
            ["rethrow"] = JsonSerializer.SerializeToElement(true),
        }.ToHashMap();
        var throwProps = new Dictionary<string, JsonElement>
        {
            ["errorType"] = JsonSerializer.SerializeToElement("FatalError"),
            ["message"] = JsonSerializer.SerializeToElement("Fatal!"),
        }.ToHashMap();

        var def = new WorkflowDefinition(
            Id: Guid.NewGuid(), Name: "tc-rethrow", Description: null, Version: new Version(1, 0),
            Nodes: Arr.create(
                new NodeDefinition("tc", "builtin.trycatch", "TryCatch", tcProps),
                new NodeDefinition("throwNode", "builtin.throw", "Throw", throwProps),
                new NodeDefinition("finallyNode", "mod.finally", "Finally", HashMap<string, JsonElement>.Empty)),
            Connections: Arr.create(
                new ConnectionDefinition("tc", "try", "throwNode", "input"),
                new ConnectionDefinition("tc", "finally", "finallyNode", "input")),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var parent = CreateTestProbe("tc-rethrow-parent");
        var exec = parent.ChildActorOf(
            WorkflowExecutor.Props(Guid.NewGuid(), def, new Dictionary<string, object?>(), sp),
            "tc-rethrow-exec");

        exec.Tell(new StartExecution(Guid.NewGuid()));

        var msg = parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(10));
        msg.Should().BeOfType<WorkflowFailed>(
            because: "rethrow=true must re-raise the original error — workflow should FAIL~ ❗");
        finallyCounter.Count.Should().Be(1, "finally still runs even when rethrowing~ 🧹");
    }

    /// <summary>
    /// ThrowModule inside a try block produces a structured WorkflowUserException
    /// whose error type is accessible via the catch error payload~ 💥🛡️
    /// </summary>
    [Fact]
    public async Task ThrowModule_ProducesWorkflowUserException_WithCorrectType()
    {
        var ctx = BuildThrowContext(inputs: new Dictionary<string, object?>
        {
            ["errorType"] = "MyCustomError",
            ["message"] = "oops",
        });

        var ex = await Assert.ThrowsAsync<WorkflowUserException>(() => _throwModule.ExecuteAsync(ctx));
        ex.ErrorType.Should().Be("MyCustomError");

        var error = WorkflowError.FromException(ex, nodeId: "throw-node");
        error.ErrorType.Should().Be("MyCustomError",
            because: "WorkflowError.FromException should use WorkflowUserException.ErrorType~ 🏷️");
        error.NodeId.Should().Be("throw-node",
            because: "NodeId should be stamped on the error~ 🆔");
    }

    // ── Stubs & Helpers ──────────────────────────────────────────────────────────────────

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>Thread-safe counting module that records how many times it ran~ 🔢</summary>
    private sealed class CountingModule
    {
        private int _count;
        public int Count => _count;
        public void Increment() => System.Threading.Interlocked.Increment(ref _count);
    }

    /// <summary>Module wrapper that delegates to a CountingModule and increments its counter~ 🌿</summary>
    private sealed class BranchedModule : IWorkflowModule
    {
        private readonly CountingModule _counter;
        public BranchedModule(string moduleId, CountingModule counter)
        {
            ModuleId = moduleId;
            _counter = counter;
        }

        public string ModuleId { get; }
        public string DisplayName => ModuleId;
        public string Category => "Test";
        public string Description => "Test counting stub~ 🔢";
        public string Icon => "🔢";
        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Inputs: Arr.create(PortDefinition.Create<object>("input", isRequired: false)),
            Outputs: Arr.create(PortDefinition.Create<object>("output", isRequired: false)),
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
        {
            _counter.Increment();
            return Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = "ok" }));
        }
    }
}



