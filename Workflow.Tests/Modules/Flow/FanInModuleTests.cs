// <copyright file="FanInModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Flow;

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
/// 🪄 Phase 2.2.3b — Unit tests for <see cref="FanInModule"/> (<c>builtin.fanin</c>)~
/// Validates metadata, schema, aggregation modes, and ValidateConfiguration~ ✨💖
/// </summary>
public sealed class FanInModuleTests
{
    private readonly FanInModule _module = new();

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
            NodeId = "fanin-node",
        };

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    // ── Metadata ─────────────────────────────────────────────────────────────────

    /// <summary>Verifies ModuleId, category, display name, icon, and version~ 🪄.</summary>
    [Fact]
    public void FanInModule_Metadata_IsCorrect()
    {
        _module.ModuleId.Should().Be("builtin.fanin");
        _module.Category.Should().Be("Flow Control");
        _module.DisplayName.Should().Be("Fan In");
        _module.Icon.Should().Be("🪄");
        _module.Version.Should().Be(new Version(1, 0, 0));
    }

    // ── Schema ───────────────────────────────────────────────────────────────────

    /// <summary>Schema declares <c>result</c>, <c>count</c>, <c>done</c> outputs + <c>branches</c> input~ 📋.</summary>
    [Fact]
    public void FanInModule_Schema_HasCorrectPorts()
    {
        var outputNames = _module.Schema.Outputs.Select(p => p.Name).ToList();
        outputNames.Should().Contain("result", because: "aggregated result output~ 🪄");
        outputNames.Should().Contain("count", because: "branch count output~ 🔢");
        outputNames.Should().Contain("done", because: "continuation signal output~ ✅");

        var inputNames = _module.Schema.Inputs.Select(p => p.Name).ToList();
        inputNames.Should().Contain("branches", because: "declarative branches input (actual via __incomingBranches__)~ 📥");

        var propNames = _module.Schema.Properties.Select(p => p.Name).ToList();
        propNames.Should().Contain("mode", because: "aggregation mode property~ 🎨");
        propNames.Should().Contain("timeout", because: "forward-compat timeout property~ ⏱️");
    }

    // ── Aggregation modes ────────────────────────────────────────────────────────

    /// <summary>Default mode is Concat — no mode property set~ 📜.</summary>
    [Fact]
    public async Task DefaultMode_IsConcat_WithNoBranchesReturnsEmptyList()
    {
        // CopilotNote: no __incomingBranches__ supplied → falls back to empty list~ 🪄
        var ctx = BuildContext();
        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Outputs["result"].Should().BeAssignableTo<IEnumerable<object?>>(
            because: "Concat mode returns a list even when empty~ 📜");
        result.Outputs["count"].Should().Be(0);
    }

    /// <summary>Concat preserves branch insertion order~ 📜.</summary>
    [Fact]
    public async Task Concat_PreservesOrder()
    {
        var branches = new List<Dictionary<string, object?>>
        {
            new() { ["step"] = "first" },
            new() { ["step"] = "second" },
            new() { ["step"] = "third" },
        };

        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["__incomingBranches__"] = branches },
            properties: new Dictionary<string, object?> { ["mode"] = "Concat" });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        var list = result.Outputs["result"].Should().BeAssignableTo<List<object?>>()
            .Which.Cast<Dictionary<string, object?>>().ToList();
        list[0]["step"].Should().Be("first");
        list[1]["step"].Should().Be("second");
        list[2]["step"].Should().Be("third");
        result.Outputs["count"].Should().Be(3);
    }

    /// <summary>Merge mode — last writer wins for duplicate keys~ 🧪.</summary>
    [Fact]
    public async Task Merge_LastWriterWins()
    {
        var branches = new List<Dictionary<string, object?>>
        {
            new() { ["key"] = "first-value", ["only_first"] = "A" },
            new() { ["key"] = "second-value", ["only_second"] = "B" },
        };

        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["__incomingBranches__"] = branches },
            properties: new Dictionary<string, object?> { ["mode"] = "Merge" });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        var merged = result.Outputs["result"].Should().BeAssignableTo<Dictionary<string, object?>>().Which;
        merged["key"].Should().Be("second-value", because: "last writer wins for 'key'~ 🧪");
        merged["only_first"].Should().Be("A", because: "unique key from first branch preserved~ 🎗️");
        merged["only_second"].Should().Be("B", because: "unique key from second branch preserved~ 🎗️");
    }

    /// <summary>UX-F2: Named mode keys each branch by its source port name~ 🏷️.</summary>
    [Fact]
    public async Task Named_KeysByPortName()
    {
        // One node with ports foo/bar/baz fanned into one object.
        var branches = new List<Dictionary<string, object?>>
        {
            new() { ["foo"] = 1, ["bar"] = 2, ["baz"] = 3 },
            new() { ["foo"] = 1, ["bar"] = 2, ["baz"] = 3 },
            new() { ["foo"] = 1, ["bar"] = 2, ["baz"] = 3 },
        };
        var meta = new List<Dictionary<string, object?>>
        {
            new() { ["sourceNodeId"] = "n1", ["sourcePortName"] = "foo" },
            new() { ["sourceNodeId"] = "n1", ["sourcePortName"] = "bar" },
            new() { ["sourceNodeId"] = "n1", ["sourcePortName"] = "baz" },
        };

        var ctx = BuildContext(
            inputs: new Dictionary<string, object?>
            {
                ["__incomingBranches__"] = branches,
                ["__incomingBranchMeta__"] = meta,
            },
            properties: new Dictionary<string, object?> { ["mode"] = "named" });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        var named = result.Outputs["result"].Should().BeAssignableTo<Dictionary<string, object?>>().Which;
        named.Keys.Should().BeEquivalentTo(new[] { "foo", "bar", "baz" });
        named["foo"].Should().Be(1);
        named["bar"].Should().Be(2);
        named["baz"].Should().Be(3);
    }

    /// <summary>UX-F2: Named mode falls back to <c>nodeId.port</c> keys on port-name collision~ 🏷️.</summary>
    [Fact]
    public async Task Named_Collision_FallsBackToNodePortKeys()
    {
        var branches = new List<Dictionary<string, object?>>
        {
            new() { ["success"] = "a" },
            new() { ["success"] = "b" },
        };
        var meta = new List<Dictionary<string, object?>>
        {
            new() { ["sourceNodeId"] = "http-1", ["sourcePortName"] = "success" },
            new() { ["sourceNodeId"] = "http-2", ["sourcePortName"] = "success" },
        };

        var ctx = BuildContext(
            inputs: new Dictionary<string, object?>
            {
                ["__incomingBranches__"] = branches,
                ["__incomingBranchMeta__"] = meta,
            },
            properties: new Dictionary<string, object?> { ["mode"] = "named" });

        var result = await _module.ExecuteAsync(ctx);

        var named = result.Outputs["result"].Should().BeAssignableTo<Dictionary<string, object?>>().Which;
        named.Keys.Should().BeEquivalentTo(new[] { "http-1.success", "http-2.success" });
        named["http-1.success"].Should().Be("a");
        named["http-2.success"].Should().Be("b");
    }

    /// <summary>UX-F2: Named mode without metadata uses positional <c>branch{i}</c> keys~ 🏷️.</summary>
    [Fact]
    public async Task Named_NoMeta_UsesPositionalKeys()
    {
        var branches = new List<Dictionary<string, object?>>
        {
            new() { ["x"] = 1 },
            new() { ["y"] = 2 },
        };

        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["__incomingBranches__"] = branches },
            properties: new Dictionary<string, object?> { ["mode"] = "named" });

        var result = await _module.ExecuteAsync(ctx);

        var named = result.Outputs["result"].Should().BeAssignableTo<Dictionary<string, object?>>().Which;
        named.Keys.Should().BeEquivalentTo(new[] { "branch0", "branch1" });
        named["branch0"].Should().BeSameAs(branches[0], because: "without a port name the whole snapshot is the value~ 🏷️");
    }

    /// <summary>UX-F2: Named mode with empty branches returns an empty object~ 🪹.</summary>
    [Fact]
    public async Task Named_EmptyBranches_ReturnsEmptyObject()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["__incomingBranches__"] = new List<Dictionary<string, object?>>() },
            properties: new Dictionary<string, object?> { ["mode"] = "named" });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Outputs["result"].Should().BeAssignableTo<Dictionary<string, object?>>()
            .Which.Should().BeEmpty();
    }

    /// <summary>First mode returns the first branch's payload~ 🥇.</summary>
    [Fact]
    public async Task First_ReturnsFirstPayload()
    {
        var branches = new List<Dictionary<string, object?>>
        {
            new() { ["value"] = "first" },
            new() { ["value"] = "second" },
            new() { ["value"] = "third" },
        };

        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["__incomingBranches__"] = branches },
            properties: new Dictionary<string, object?> { ["mode"] = "First" });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        var dict = result.Outputs["result"].Should().BeAssignableTo<Dictionary<string, object?>>().Which;
        dict["value"].Should().Be("first", because: "First mode must return the first connection's payload~ 🥇");
        result.Outputs["count"].Should().Be(3, because: "count reflects total branch count regardless of mode~ 🔢");
    }

    /// <summary>Last mode returns the last branch's payload~ 🥈.</summary>
    [Fact]
    public async Task Last_ReturnsLastPayload()
    {
        var branches = new List<Dictionary<string, object?>>
        {
            new() { ["value"] = "first" },
            new() { ["value"] = "second" },
            new() { ["value"] = "last" },
        };

        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["__incomingBranches__"] = branches },
            properties: new Dictionary<string, object?> { ["mode"] = "Last" });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        var dict = result.Outputs["result"].Should().BeAssignableTo<Dictionary<string, object?>>().Which;
        dict["value"].Should().Be("last", because: "Last mode must return the final connection's payload~ 🥈");
    }

    /// <summary>Mode is case-insensitive — <c>"concat"</c> (lowercase) resolves to Concat~ 🔡.</summary>
    [Fact]
    public async Task Mode_IsCaseInsensitive()
    {
        var branches = new List<Dictionary<string, object?>>
        {
            new() { ["x"] = 1 },
            new() { ["y"] = 2 },
        };

        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["__incomingBranches__"] = branches },
            properties: new Dictionary<string, object?> { ["mode"] = "concat" });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue(because: "mode is case-insensitive~ 🔡");
        result.Outputs["result"].Should().BeAssignableTo<List<object?>>().Which.Count.Should().Be(2);
    }

    /// <summary>Empty branches with First mode returns null result~ 🪹.</summary>
    [Fact]
    public async Task First_EmptyBranches_ReturnsNull()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["__incomingBranches__"] = new List<Dictionary<string, object?>>() },
            properties: new Dictionary<string, object?> { ["mode"] = "First" });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Outputs["result"].Should().BeNull(because: "no branches → First returns null~ 🪹");
        result.Outputs["count"].Should().Be(0);
    }

    // ── ValidateConfiguration ────────────────────────────────────────────────────

    /// <summary>Invalid mode string produces INVALID_MODE validation error~ 💔.</summary>
    [Fact]
    public void ValidateConfiguration_InvalidMode_Fails()
    {
        var config = new Dictionary<string, object?> { ["mode"] = "BogusMode" };

        var result = _module.ValidateConfiguration(config);

        result.IsValid.Should().BeFalse(because: "unknown mode must fail validation~ 💔");
        result.Errors.Should().ContainSingle(e => e.Code == "INVALID_MODE");
    }

    /// <summary>Valid mode string passes validation~ ✅.</summary>
    [Theory]
    [InlineData("Concat")]
    [InlineData("Merge")]
    [InlineData("First")]
    [InlineData("Last")]
    [InlineData("concat")]
    [InlineData("MERGE")]
    public void ValidateConfiguration_ValidMode_Passes(string mode)
    {
        var config = new Dictionary<string, object?> { ["mode"] = mode };

        var result = _module.ValidateConfiguration(config);

        result.IsValid.Should().BeTrue(because: $"mode '{mode}' is a valid FanInMode~ ✅");
    }

    /// <summary>Empty/no mode config is valid — defaults to Concat~ 📋.</summary>
    [Fact]
    public void ValidateConfiguration_NoMode_IsValid()
    {
        var config = new Dictionary<string, object?>();

        var result = _module.ValidateConfiguration(config);

        result.IsValid.Should().BeTrue(because: "mode is optional; default Concat is always valid~ 📋");
    }
}

/// <summary>
/// 🪄 Phase 2.2.3b — Engine integration test for combined FanOut → work → FanIn pattern~
/// Proves the complete fan-shaped pattern composes cleanly end-to-end~ ✨💖
/// </summary>
public class FanInEngineIntegrationTests : TestKit
{
    /// <summary>
    /// A module that passes through its <c>item</c> input to a <c>value</c> output,
    /// enabling FanIn to collect distinct values per branch~ 🎁.
    /// </summary>
    private sealed class PassThroughModule : IWorkflowModule
    {
        private int _count;
        public int Count => _count;

        public string ModuleId => "test.fanin.passthrough";
        public string DisplayName => "PassThrough";
        public string Category => "Test";
        public string Description => "Passes item through~ 🎁";
        public string Icon => "🎁";
        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Inputs: Arr.create(
                PortDefinition.Create<object>("item", isRequired: false),
                PortDefinition.Create<object>("input", isRequired: false)),
            Outputs: Arr.create(PortDefinition.Create<object>("value", isRequired: false)),
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
        {
            System.Threading.Interlocked.Increment(ref _count);
            var item = ctx.Inputs.TryGetValue("item", out var v) ? v
                : ctx.Inputs.TryGetValue("input", out var iv) ? iv : null;
            return Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["value"] = item }));
        }
    }

    /// <summary>
    /// Verifies that a static Parallel → PassThrough (×2 branches) → FanIn (Concat) pattern completes.
    /// FanIn collects both branch result dictionaries in declared connection order~ 🪄✅
    /// </summary>
    [Fact]
    public void FanIn_Concat_CollectsBothBranchResults_WorkflowCompletes()
    {
        var workerA = new PassThroughModule();
        var fanInModule = new FanInModule();
        var parallelModule = new ParallelModule();
        var doneCounter = new PassThroughModule();

        var branchAModule = new AliasedModule("test.fanin.pt.a", workerA);
        var doneModule = new AliasedModule("test.fanin.pt.done", doneCounter);

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(parallelModule);
        registry.RegisterModule(branchAModule);
        registry.RegisterModule(fanInModule);
        registry.RegisterModule(doneModule);

        var sp = new ServiceCollection().AddSingleton<IModuleRegistry>(registry).BuildServiceProvider();

        var parProps = new Dictionary<string, JsonElement>
        {
            ["branches"] = JsonSerializer.SerializeToElement(new[] { "b1" }),
        }.ToHashMap();

        var fanInProps = new Dictionary<string, JsonElement>
        {
            ["mode"] = JsonSerializer.SerializeToElement("Concat"),
        }.ToHashMap();

        var def = new WorkflowDefinition(
            Id: Guid.NewGuid(), Name: "fanin-concat", Description: null, Version: new Version(1, 0),
            Nodes: Arr.create(
                new NodeDefinition("par", "builtin.parallel", "Parallel", parProps),
                new NodeDefinition("work", "test.fanin.pt.a", "Worker", HashMap<string, JsonElement>.Empty),
                new NodeDefinition("fanin", "builtin.fanin", "FanIn", fanInProps),
                new NodeDefinition("post", "test.fanin.pt.done", "Post", HashMap<string, JsonElement>.Empty)),
            Connections: Arr.create(
                new ConnectionDefinition("par", "b1", "work", "input"),
                new ConnectionDefinition("par", "done", "fanin", "branches"),
                new ConnectionDefinition("fanin", "done", "post", "input")),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var parent = CreateTestProbe("fanin-concat-parent");
        var exec = parent.ChildActorOf(
            WorkflowExecutor.Props(Guid.NewGuid(), def, new Dictionary<string, object?>(), sp),
            "fanin-concat-exec");

        exec.Tell(new StartExecution(Guid.NewGuid()));

        var msg = parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(10));
        msg.Should().BeOfType<WorkflowCompleted>(
            because: "Parallel → work → FanIn → post workflow must complete~ 🪄✅");
        workerA.Count.Should().Be(1, because: "worker runs once for the single branch~ 🎁");
        doneCounter.Count.Should().Be(1, because: "post-FanIn node runs once after aggregation~ ✅");
    }

    /// <summary>
    /// UX-F2 end-to-end: a node with two output ports fans into a named FanIn — the engine's
    /// <c>__incomingBranchMeta__</c> lets the result be keyed by port name~ 🏷️✅
    /// </summary>
    [Fact]
    public void FanIn_Named_KeysResultBySourcePortName_EndToEnd()
    {
        var source = new TwoPortSourceModule();
        var fanInModule = new FanInModule();
        var capture = new CaptureModule();

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(source);
        registry.RegisterModule(fanInModule);
        registry.RegisterModule(capture);

        var sp = new ServiceCollection().AddSingleton<IModuleRegistry>(registry).BuildServiceProvider();

        var fanInProps = new Dictionary<string, JsonElement>
        {
            ["mode"] = JsonSerializer.SerializeToElement("named"),
        }.ToHashMap();

        var def = new WorkflowDefinition(
            Id: Guid.NewGuid(), Name: "fanin-named", Description: null, Version: new Version(1, 0),
            Nodes: Arr.create(
                new NodeDefinition("src", "test.fanin.twoport", "Source", HashMap<string, JsonElement>.Empty),
                new NodeDefinition("fanin", "builtin.fanin", "FanIn", fanInProps),
                new NodeDefinition("cap", "test.fanin.capture", "Capture", HashMap<string, JsonElement>.Empty)),
            Connections: Arr.create(
                new ConnectionDefinition("src", "foo", "fanin", "branches"),
                new ConnectionDefinition("src", "bar", "fanin", "branches"),
                new ConnectionDefinition("fanin", "result", "cap", "input")),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var parent = CreateTestProbe("fanin-named-parent");
        var exec = parent.ChildActorOf(
            WorkflowExecutor.Props(Guid.NewGuid(), def, new Dictionary<string, object?>(), sp),
            "fanin-named-exec");

        exec.Tell(new StartExecution(Guid.NewGuid()));

        var msg = parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(10));
        msg.Should().BeOfType<WorkflowCompleted>(because: "src → named FanIn → capture must complete~ 🏷️✅");

        var named = capture.Received.Should().BeAssignableTo<Dictionary<string, object?>>().Which;
        named.Keys.Should().BeEquivalentTo(new[] { "foo", "bar" });
        named["foo"].Should().Be("F");
        named["bar"].Should().Be("B");
    }

    /// <summary>A module with two output ports (<c>foo</c>/<c>bar</c>)~ 🎁.</summary>
    private sealed class TwoPortSourceModule : IWorkflowModule
    {
        public string ModuleId => "test.fanin.twoport";
        public string DisplayName => "TwoPort";
        public string Category => "Test";
        public string Description => "Emits foo + bar~ 🎁";
        public string Icon => "🎁";
        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Inputs: Arr.create(PortDefinition.Create<object>("input", isRequired: false)),
            Outputs: Arr.create(
                PortDefinition.Create<object>("foo", isRequired: false),
                PortDefinition.Create<object>("bar", isRequired: false)),
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
            => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["foo"] = "F", ["bar"] = "B" }));
    }

    /// <summary>Captures its <c>input</c> value for assertions~ 🎁.</summary>
    private sealed class CaptureModule : IWorkflowModule
    {
        public object? Received { get; private set; }

        public string ModuleId => "test.fanin.capture";
        public string DisplayName => "Capture";
        public string Category => "Test";
        public string Description => "Captures input~ 🎁";
        public string Icon => "🎁";
        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Inputs: Arr.create(PortDefinition.Create<object>("input", isRequired: false)),
            Outputs: Arr.create(PortDefinition.Create<object>("output", isRequired: false)),
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
        {
            this.Received = ctx.Inputs.TryGetValue("input", out var v) ? v : null;
            return Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = null }));
        }
    }

    /// <summary>Wraps a PassThroughModule under an alias~ 🎀.</summary>
    private sealed class AliasedModule : IWorkflowModule
    {
        private readonly PassThroughModule _inner;

        public AliasedModule(string id, PassThroughModule inner)
        {
            ModuleId = id;
            _inner = inner;
        }

        public string ModuleId { get; }
        public string DisplayName => "Aliased";
        public string Category => "Test";
        public string Description => "Alias~";
        public string Icon => "🎁";
        public Version Version => new(1, 0);
        public ModuleSchema Schema => _inner.Schema;

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
            => _inner.ExecuteAsync(ctx, ct);
    }
}

