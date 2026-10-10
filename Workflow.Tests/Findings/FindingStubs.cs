// <copyright file="FindingStubs.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Findings;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Core.Models;
using Workflow.Modules;
using Workflow.Modules.Abstractions;

/// <summary>
/// Shared builders and stub modules for the architecture-finding tests
/// (see <c>docs/architecture/README.md#findings-at-a-glance</c>).
/// </summary>
/// <remarks>
/// Every stub is in-memory only: no file system, no network, no processes. Delays are short and
/// always honour the cancellation token.
/// </remarks>
internal static class FindingDefs
{
    /// <summary>The xUnit trait used to include or exclude every finding test.</summary>
    public const string Category = "Finding";

    /// <summary>Default wait for a run to reach a terminal state.</summary>
    public static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(15);

    public static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value);

    public static NodeDefinition Node(
        string id,
        string moduleId,
        IEnumerable<(string Key, object? Value)>? properties = null,
        ErrorHandling? errorHandling = null,
        RetryPolicy? retry = null,
        HashMap<string, string>? metadata = null)
    {
        var props = HashMap<string, JsonElement>.Empty;
        foreach (var (key, value) in properties ?? Enumerable.Empty<(string, object?)>())
        {
            props = props.AddOrUpdate(key, Json(value));
        }

        return new NodeDefinition(
            Id: id,
            ModuleId: moduleId,
            Name: id,
            Properties: props,
            ErrorHandling: errorHandling,
            RetryPolicy: retry,
            Metadata: metadata);
    }

    public static ConnectionDefinition Edge(string from, string fromPort, string to, string toPort, string? condition = null)
        => new(from, fromPort, to, toPort, condition);

    public static WorkflowDefinition Definition(
        IEnumerable<NodeDefinition> nodes,
        IEnumerable<ConnectionDefinition>? connections = null,
        IEnumerable<VariableDefinition>? variables = null)
    {
        var vars = HashMap<string, VariableDefinition>.Empty;
        foreach (var v in variables ?? Enumerable.Empty<VariableDefinition>())
        {
            vars = vars.AddOrUpdate(v.Name, v);
        }

        return new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "finding-" + Guid.NewGuid().ToString("N")[..8],
            Description: null,
            Version: new Version(1, 0),
            Nodes: nodes.ToArr(),
            Connections: (connections ?? Enumerable.Empty<ConnectionDefinition>()).ToArr(),
            Variables: vars);
    }

    /// <summary>Builds a service provider with a validation-free registry holding the given modules.</summary>
    public static IServiceProvider Services(
        IEnumerable<IWorkflowModule> modules,
        Action<IServiceCollection>? configure = null)
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        foreach (var module in modules)
        {
            registry.RegisterModule(module);
        }

        var services = new ServiceCollection();
        services.AddSingleton<IModuleRegistry>(registry);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }
}

/// <summary>Common identity members for the stubs below.</summary>
internal abstract class FindingModuleBase : IWorkflowModule
{
    protected FindingModuleBase(string moduleId, Version? version = null)
    {
        this.ModuleId = moduleId;
        this.Version = version ?? new Version(1, 0);
    }

    public string ModuleId { get; }

    public string DisplayName => this.ModuleId;

    public string Category => "Test";

    public string Description => "Finding test stub";

    public string Icon => "🧪";

    public Version Version { get; }

    public abstract ModuleSchema Schema { get; }

    /// <summary>Gets how many times <see cref="ExecuteAsync"/> ran.</summary>
    public int Calls => this.calls;

    private int calls;

    public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref this.calls);
        return this.RunAsync(context, cancellationToken);
    }

    protected abstract Task<ModuleResult> RunAsync(ModuleExecutionContext context, CancellationToken cancellationToken);

    protected static Arr<PortDefinition> Ports(params string[] names)
        => names.Select(n => PortDefinition.Create<object>(n, isRequired: false)).ToArr();
}

/// <summary>Input <c>input</c>, output <c>output</c>; optionally waits before completing.</summary>
internal sealed class PassModule : FindingModuleBase
{
    private readonly int delayMs;

    public PassModule(string moduleId, int delayMs = 0)
        : base(moduleId) => this.delayMs = delayMs;

    public override ModuleSchema Schema => new(Ports("input"), Ports("output"), Arr<ModulePropertyDefinition>.Empty);

    protected override async Task<ModuleResult> RunAsync(ModuleExecutionContext context, CancellationToken cancellationToken)
    {
        if (this.delayMs > 0)
        {
            await Task.Delay(this.delayMs, cancellationToken).ConfigureAwait(false);
        }

        return ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = context.NodeId + "-done" });
    }
}

/// <summary>Emits a fixed set of outputs.</summary>
internal sealed class OutputModule : FindingModuleBase
{
    private readonly Dictionary<string, object?> outputs;

    public OutputModule(string moduleId, Dictionary<string, object?> outputs)
        : base(moduleId) => this.outputs = outputs;

    public override ModuleSchema Schema
        => new(Ports("input"), Ports(this.outputs.Keys.ToArray()), Arr<ModulePropertyDefinition>.Empty);

    protected override Task<ModuleResult> RunAsync(ModuleExecutionContext context, CancellationToken cancellationToken)
        => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?>(this.outputs)));
}

/// <summary>Fails the first <c>failTimes</c> calls, then succeeds. <c>int.MaxValue</c> = always fails.</summary>
internal sealed class FlakyModule : FindingModuleBase
{
    private readonly int failTimes;

    public FlakyModule(string moduleId, int failTimes = int.MaxValue)
        : base(moduleId) => this.failTimes = failTimes;

    public override ModuleSchema Schema => new(Ports("input"), Ports("output"), Arr<ModulePropertyDefinition>.Empty);

    protected override Task<ModuleResult> RunAsync(ModuleExecutionContext context, CancellationToken cancellationToken)
        => Task.FromResult(this.Calls <= this.failTimes
            ? ModuleResult.Fail($"intentional failure #{this.Calls}", new InvalidOperationException("intentional failure"))
            : ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = "recovered" }));
}

/// <summary>Activates only <c>activePorts</c> after an optional delay.</summary>
internal sealed class PortModule : FindingModuleBase
{
    private readonly string[] activePorts;
    private readonly string[] declaredPorts;
    private readonly int delayMs;

    public PortModule(string moduleId, string[] activePorts, string[] declaredPorts, int delayMs = 0)
        : base(moduleId)
    {
        this.activePorts = activePorts;
        this.declaredPorts = declaredPorts;
        this.delayMs = delayMs;
    }

    public override ModuleSchema Schema => new(Ports("input"), Ports(this.declaredPorts), Arr<ModulePropertyDefinition>.Empty);

    protected override async Task<ModuleResult> RunAsync(ModuleExecutionContext context, CancellationToken cancellationToken)
    {
        if (this.delayMs > 0)
        {
            await Task.Delay(this.delayMs, cancellationToken).ConfigureAwait(false);
        }

        var outputs = this.activePorts.ToDictionary(p => p, _ => (object?)true);
        return ModuleResult.WithActivePorts(outputs, this.activePorts);
    }
}

/// <summary>A foreach-style construct: asks the engine to loop <c>loopBody</c> over fixed items.</summary>
internal sealed class LoopModule : FindingModuleBase
{
    private readonly IReadOnlyList<object?> items;

    public LoopModule(string moduleId, IReadOnlyList<object?> items)
        : base(moduleId) => this.items = items;

    public override ModuleSchema Schema => new(Ports("input"), Ports("loopBody", "done"), Arr<ModulePropertyDefinition>.Empty);

    protected override Task<ModuleResult> RunAsync(ModuleExecutionContext context, CancellationToken cancellationToken)
        => Task.FromResult(ModuleResult.WithLoop(
            new Dictionary<string, object?>(),
            new LoopRequest { Items = this.items, LoopBodyPort = "loopBody", DonePort = "done" }));
}

/// <summary>One recorded call of a <see cref="CaptureModule"/>.</summary>
internal sealed record CapturedCall(
    IReadOnlyDictionary<string, object?> Inputs,
    IReadOnlyDictionary<string, object?> Properties,
    IReadOnlyDictionary<string, object?> Variables);

/// <summary>Records every context it runs with. Declared properties may opt into templates.</summary>
internal sealed class CaptureModule : FindingModuleBase
{
    private readonly string[] inputPorts;
    private readonly string[] templatedProperties;

    public CaptureModule(string moduleId, string[]? inputPorts = null, string[]? templatedProperties = null)
        : base(moduleId)
    {
        this.inputPorts = inputPorts ?? new[] { "input" };
        this.templatedProperties = templatedProperties ?? Array.Empty<string>();
    }

    public ConcurrentQueue<CapturedCall> Captured { get; } = new();

    public override ModuleSchema Schema => new(
        Ports(this.inputPorts),
        Ports("output"),
        this.templatedProperties
            .Select(p => new ModulePropertyDefinition(p, p, typeof(object), SupportsTemplates: true))
            .ToArr());

    protected override Task<ModuleResult> RunAsync(ModuleExecutionContext context, CancellationToken cancellationToken)
    {
        this.Captured.Enqueue(new CapturedCall(
            new Dictionary<string, object?>(context.Inputs),
            new Dictionary<string, object?>(context.Properties),
            new Dictionary<string, object?>(context.Variables)));
        return Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = context.NodeId + "-captured" }));
    }
}

/// <summary>Writes <c>variableName</c> = the node's <c>item</c> input (or a fixed value) as a variable update.</summary>
internal sealed class VariableSetterModule : FindingModuleBase
{
    private readonly string variableName;
    private readonly object? fixedValue;

    public VariableSetterModule(string moduleId, string variableName, object? fixedValue = null)
        : base(moduleId)
    {
        this.variableName = variableName;
        this.fixedValue = fixedValue;
    }

    public override ModuleSchema Schema => new(Ports("input"), Ports("output"), Arr<ModulePropertyDefinition>.Empty);

    protected override Task<ModuleResult> RunAsync(ModuleExecutionContext context, CancellationToken cancellationToken)
    {
        var value = this.fixedValue ?? (context.Inputs.TryGetValue("item", out var item) ? item : null);
        return Task.FromResult(ModuleResult.Ok(
            new Dictionary<string, object?> { ["output"] = value },
            new Dictionary<string, object?> { [this.variableName] = value }));
    }
}

/// <summary>Counts calls and waits a little per call (honouring cancellation).</summary>
internal sealed class SlowCounterModule : FindingModuleBase
{
    private readonly int delayMs;

    public SlowCounterModule(string moduleId, int delayMs)
        : base(moduleId) => this.delayMs = delayMs;

    public override ModuleSchema Schema => new(Ports("input"), Ports("output"), Arr<ModulePropertyDefinition>.Empty);

    protected override async Task<ModuleResult> RunAsync(ModuleExecutionContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(this.delayMs, cancellationToken).ConfigureAwait(false);
        return ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = this.Calls });
    }
}

// ── Streaming stubs ────────────────────────────────────────────────────────────────────────────

/// <summary>Helpers for building and reading <see cref="StreamItem"/>s.</summary>
internal static class StreamItems
{
    public static StreamItem Of(string value, long index) => StreamItem.FromJson(FindingDefs.Json(value), index);

    public static string? Text(StreamItem item) => item.Payload is JsonPayload json ? json.Value.GetString() : null;
}

/// <summary>A streaming source yielding fixed string payloads on output port <c>items</c>.</summary>
internal sealed class StreamSourceModule : FindingModuleBase, IStreamingWorkflowModule
{
    private readonly string[] payloads;

    public StreamSourceModule(string moduleId, string[] payloads, Version? version = null)
        : base(moduleId, version) => this.payloads = payloads;

    public override ModuleSchema Schema => new(
        Arr<PortDefinition>.Empty,
        Arr.create(PortDefinition.CreateStreaming("items", isRequired: false)),
        Arr<ModulePropertyDefinition>.Empty);

    public async IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
        ModuleExecutionContext context,
        IAsyncEnumerable<StreamItem> input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < this.payloads.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return StreamItems.Of(this.payloads[i], i);
            await Task.Yield();
        }
    }

    protected override Task<ModuleResult> RunAsync(ModuleExecutionContext context, CancellationToken cancellationToken)
        => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["items"] = this.payloads.ToList() }));
}

/// <summary>A streaming terminal that records what it receives and how it was invoked.</summary>
internal sealed class CollectingTerminalModule : FindingModuleBase, IStreamTerminalModule
{
    private int streamCalls;

    public CollectingTerminalModule(string moduleId)
        : base(moduleId)
    {
    }

    public ConcurrentQueue<string?> Received { get; } = new();

    /// <summary>Gets how many times the streaming entry point ran.</summary>
    public int StreamCalls => this.streamCalls;

    public override ModuleSchema Schema => new(
        Arr.create(PortDefinition.CreateStreaming("items", isRequired: false)),
        Ports("count"),
        Arr<ModulePropertyDefinition>.Empty);

    public async Task<ModuleResult> ExecuteTerminalAsync(
        ModuleExecutionContext context,
        IAsyncEnumerable<StreamItem> input,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref this.streamCalls);
        var count = 0;
        await foreach (var item in input.WithCancellation(cancellationToken))
        {
            this.Received.Enqueue(StreamItems.Text(item));
            count++;
        }

        return ModuleResult.Ok(new Dictionary<string, object?> { ["count"] = count });
    }

    public async IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
        ModuleExecutionContext context,
        IAsyncEnumerable<StreamItem> input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await this.ExecuteTerminalAsync(context, input, cancellationToken);
        yield break;
    }

    protected override Task<ModuleResult> RunAsync(ModuleExecutionContext context, CancellationToken cancellationToken)
        => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["count"] = 0 }));
}
