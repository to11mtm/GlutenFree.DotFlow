// <copyright file="StreamRegionPinnedVersionTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine.Streaming;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
using Xunit;

/// <summary>
/// §9.4 (streaming) — a node pinned to a module version (<c>Metadata["moduleVersion"]</c>) runs that
/// version inside a streaming region too, and region detection looks at the pinned version's ports.
/// All module resolution now goes through <see cref="ModuleResolution"/>.
/// </summary>
public sealed class StreamRegionPinnedVersionTests : TestKit
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public void PinnedVersion_RunsInsideTheRegion()
    {
        var collector = new DualCollector();
        var terminal = this.Run(Pinned("1.0"), new StreamingSource(1, "v1"), new StreamingSource(2, "v2"), collector);

        terminal.Should().BeOfType<WorkflowCompleted>();
        collector.Received.Should().Equal(new[] { "v1" }, because: "the source is pinned to 1.0, so the region must run 1.0, not the latest (§9.4)");
    }

    [Fact]
    public void Unpinned_RunsTheLatestVersion()
    {
        var collector = new DualCollector();
        var terminal = this.Run(Pinned(null), new StreamingSource(1, "v1"), new StreamingSource(2, "v2"), collector);

        terminal.Should().BeOfType<WorkflowCompleted>();
        collector.Received.Should().Equal("v2");
    }

    [Fact]
    public void PinnedVersionThatDoesntExist_FailsWithTheSameClearErrorAsTheBatchPath()
    {
        var terminal = this.Run(Pinned("9.0"), new StreamingSource(1, "v1"), new StreamingSource(2, "v2"), new DualCollector());

        terminal.Should().BeOfType<WorkflowFailed>()
            .Which.Error.Message.Should().Contain("Module 'pv.src' version 9.0 is not available (missing or disabled)");
    }

    [Fact]
    public void RegionDetection_UsesThePinnedVersionsPorts()
    {
        // The collector's v2 is a streaming terminal; the pinned v1 is an ordinary batch module.
        // Detecting regions from the latest version would build a region src → collect, then fail to
        // run v1 in it ("isn't a streaming module"). With v1's ports there's no streaming edge~
        var collector = new BatchCollector();
        var terminal = this.Run(
            PinnedCollector("1.0"),
            new DualSource(),
            collector,
            new DualCollector(version: 2));

        terminal.Should().BeOfType<WorkflowCompleted>(
            because: "with v1's (non-streaming) ports there's no region; both nodes run as batch nodes ({0})",
            (terminal as WorkflowFailed)?.Error.Message);
        collector.Received.Should().Equal(new[] { "from-batch" });
    }

    private static WorkflowDefinition PinnedCollector(string version)
        => new(
            Guid.NewGuid(),
            "pinned-collector",
            null,
            new Version(1, 0),
            Arr.create(
                new NodeDefinition("src", "pv.src", "src", HashMap<string, JsonElement>.Empty),
                new NodeDefinition(
                    "collect",
                    "pv.collect",
                    "collect",
                    HashMap<string, JsonElement>.Empty,
                    Metadata: HashMap<string, string>.Empty.Add(ModuleResolution.PinnedVersionKey, version))),
            Arr.create(new ConnectionDefinition("src", "items", "collect", "items")),
            HashMap<string, VariableDefinition>.Empty);

    private static WorkflowDefinition Pinned(string? version)
    {
        var metadata = version is null ? (HashMap<string, string>?)null : HashMap<string, string>.Empty.Add(ModuleResolution.PinnedVersionKey, version);
        return new WorkflowDefinition(
            Guid.NewGuid(),
            "pinned-region",
            null,
            new Version(1, 0),
            Arr.create(
                new NodeDefinition("src", "pv.src", "src", HashMap<string, JsonElement>.Empty, Metadata: metadata),
                new NodeDefinition("collect", "pv.collect", "collect", HashMap<string, JsonElement>.Empty)),
            Arr.create(new ConnectionDefinition("src", "items", "collect", "items")),
            HashMap<string, VariableDefinition>.Empty);
    }

    private object Run(WorkflowDefinition definition, params IWorkflowModule[] modules)
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        foreach (var module in modules)
        {
            registry.RegisterModule(module);
        }

        var services = new ServiceCollection().AddSingleton<IModuleRegistry>(registry).BuildServiceProvider();
        var executionId = Guid.NewGuid();
        var parent = this.CreateTestProbe();
        var executor = parent.ChildActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), services));
        executor.Tell(new StartExecution(executionId));
        return parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, RunTimeout);
    }

    private static StreamItem Item(string text, long index) => StreamItem.FromJson(JsonSerializer.SerializeToElement(text), index);

    private abstract class TestModule : IWorkflowModule
    {
        public abstract string ModuleId { get; }

        public string DisplayName => this.ModuleId;

        public string Category => "Test";

        public string Description => "pinned-version test module";

        public string Icon => "🔢";

        public abstract Version Version { get; }

        public abstract ModuleSchema Schema { get; }

        public abstract Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default);
    }

    /// <summary>A streaming source that emits one item tagged with its version.</summary>
    private sealed class StreamingSource : TestModule, IStreamingWorkflowModule
    {
        private readonly int major;
        private readonly string tag;

        public StreamingSource(int major, string tag)
        {
            this.major = major;
            this.tag = tag;
        }

        public override string ModuleId => "pv.src";

        public override Version Version => new(this.major, 0);

        public override ModuleSchema Schema => new(
            Arr<PortDefinition>.Empty,
            Arr.create(PortDefinition.CreateStreaming("items", isRequired: false)),
            Arr<ModulePropertyDefinition>.Empty);

        public override Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(ModuleResult.Fail("streaming only"));

        public async IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
            ModuleExecutionContext context,
            IAsyncEnumerable<StreamItem> input,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return Item(this.tag, 0);
        }
    }

    /// <summary>A streaming source that also runs as an ordinary batch node (outside a region).</summary>
    private sealed class DualSource : TestModule, IStreamingWorkflowModule
    {
        public override string ModuleId => "pv.src";

        public override Version Version => new(1, 0);

        public override ModuleSchema Schema => new(
            Arr<PortDefinition>.Empty,
            Arr.create(PortDefinition.CreateStreaming("items", isRequired: false)),
            Arr<ModulePropertyDefinition>.Empty);

        public override Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["items"] = "from-batch" }));

        public async IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
            ModuleExecutionContext context,
            IAsyncEnumerable<StreamItem> input,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return Item("from-stream", 0);
        }
    }

    /// <summary>Version 1.0 of <c>pv.collect</c> as an ordinary (non-streaming) batch module.</summary>
    private sealed class BatchCollector : TestModule
    {
        public ConcurrentQueue<string?> Received { get; } = new();

        public override string ModuleId => "pv.collect";

        public override Version Version => new(1, 0);

        public override ModuleSchema Schema => new(
            Arr.create(PortDefinition.Create<object>("items", isRequired: false)),
            Arr.create(PortDefinition.Create<long>("count", isRequired: false)),
            Arr<ModulePropertyDefinition>.Empty);

        public override Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
        {
            this.Received.Enqueue(context.Inputs.TryGetValue("items", out var v) ? v?.ToString() : null);
            return Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["count"] = 1L }));
        }
    }

    /// <summary>A streaming terminal that records what it receives.</summary>
    private sealed class DualCollector : TestModule, IStreamTerminalModule
    {
        private readonly int major;

        public DualCollector(int version = 1) => this.major = version;

        public ConcurrentQueue<string?> Received { get; } = new();

        public override string ModuleId => "pv.collect";

        public override Version Version => new(this.major, 0);

        public override ModuleSchema Schema => new(
            Arr.create(PortDefinition.CreateStreaming("items", isRequired: false)),
            Arr.create(PortDefinition.Create<long>("count", isRequired: false)),
            Arr<ModulePropertyDefinition>.Empty);

        public override Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
        {
            this.Received.Enqueue(context.Inputs.TryGetValue("items", out var v) ? v?.ToString() : null);
            return Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["count"] = 1L }));
        }

        public async Task<ModuleResult> ExecuteTerminalAsync(
            ModuleExecutionContext context,
            IAsyncEnumerable<StreamItem> input,
            CancellationToken cancellationToken = default)
        {
            var count = 0L;
            await foreach (var item in input.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                this.Received.Enqueue(item.Payload is JsonPayload json ? json.Value.GetString() : null);
                count++;
            }

            return ModuleResult.Ok(new Dictionary<string, object?> { ["count"] = count });
        }

        public IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
            ModuleExecutionContext context,
            IAsyncEnumerable<StreamItem> input,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("terminal");
    }
}
