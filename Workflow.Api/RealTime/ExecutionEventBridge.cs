// <copyright file="ExecutionEventBridge.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.RealTime;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Workflow.Api.Contracts.RealTime;
using Workflow.Core.Models;
using Workflow.Engine.Messages;
using Workflow.Persistence.Abstractions;

/// <summary>
/// 📡 Phase 3.2 — Bridges the engine's Akka <c>EventStream</c> lifecycle events to SignalR group
/// broadcasts. Runs as an <see cref="IHostedService"/> in the API so <b>Workflow.Engine gains no
/// ASP.NET/SignalR dependency</b> (D2). Events are queued through a channel and processed off the
/// actor thread so a slow client never blocks the engine~ ✨.
/// </summary>
public sealed class ExecutionEventBridge : IHostedService
{
    private readonly IServiceProvider services;
    private readonly IHubContext<WorkflowHub, IWorkflowHubClient> hub;
    private readonly ILogger<ExecutionEventBridge> logger;
    private readonly Channel<object> channel;
    private readonly ConcurrentDictionary<Guid, Guid> executionToWorkflow = new();
    private readonly CancellationTokenSource cts = new();

    private ActorSystem? system;
    private IActorRef? forwarder;
    private Task? consumer;
    private int stopped;

    /// <summary>Initializes a new instance of the <see cref="ExecutionEventBridge"/> class~ 📡.</summary>
    /// <param name="services">The root service provider.</param>
    /// <param name="hub">The typed hub context for broadcasting.</param>
    /// <param name="logger">The logger.</param>
    public ExecutionEventBridge(
        IServiceProvider services,
        IHubContext<WorkflowHub, IWorkflowHubClient> hub,
        ILogger<ExecutionEventBridge> logger)
    {
        this.services = services;
        this.hub = hub;
        this.logger = logger;
        this.channel = System.Threading.Channels.Channel.CreateUnbounded<object>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        this.system = this.services.GetRequiredService<ActorSystem>();
        this.forwarder = this.system.ActorOf(
            Props.Create(() => new EventForwarder(this.channel.Writer)), "realtime-event-forwarder");

        foreach (var type in SubscribedTypes)
        {
            this.system.EventStream.Subscribe(this.forwarder, type);
        }

        this.consumer = Task.Run(() => this.ConsumeAsync(this.cts.Token));
        this.logger.LogInformation("📡 Real-time execution event bridge started~");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Idempotent: the host (and WebApplicationFactory disposal) may call StopAsync more than once~
        if (Interlocked.Exchange(ref this.stopped, 1) == 1)
        {
            return;
        }

        if (this.system is not null && this.forwarder is not null)
        {
            foreach (var type in SubscribedTypes)
            {
                this.system.EventStream.Unsubscribe(this.forwarder, type);
            }

            this.system.Stop(this.forwarder);
        }

        this.channel.Writer.TryComplete();

        try
        {
            if (!this.cts.IsCancellationRequested)
            {
                this.cts.Cancel();
            }
        }
        catch (ObjectDisposedException)
        {
            // Already disposed — nothing to cancel~
        }

        if (this.consumer is not null)
        {
            try
            {
                await this.consumer.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown~
            }
        }

        this.cts.Dispose();
        this.logger.LogInformation("📡 Real-time execution event bridge stopped~");
    }

    private static readonly Type[] SubscribedTypes =
    {
        typeof(ExecutionStateChanged),
        typeof(NodeStateChanged),
        typeof(ProgressUpdate),
        typeof(WorkflowCompleted),
        typeof(WorkflowFailed),
        typeof(NodeExecutionCompleted),
        typeof(NodeExecutionFailed),
    };

    private async Task ConsumeAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var message in this.channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await this.DispatchAsync(message, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // A broadcast failure (e.g. a dropped client) must never break the bridge~
                    this.logger.LogWarning(ex, "📡 Failed to broadcast a real-time event~");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown~
        }
    }

    private async Task DispatchAsync(object message, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        switch (message)
        {
            case ExecutionStateChanged e when e.NewState == ExecutionState.Running && e.OldState == ExecutionState.Pending:
            {
                var wf = await this.ResolveWorkflowIdAsync(e.ExecutionId, ct).ConfigureAwait(false);
                await this.BroadcastAsync(e.ExecutionId, wf, c => c.ExecutionStarted(
                    new ExecutionStartedEvent(e.ExecutionId, wf, e.Timestamp))).ConfigureAwait(false);
                break;
            }

            case WorkflowCompleted e:
            {
                var wf = await this.ResolveWorkflowIdAsync(e.ExecutionId, ct).ConfigureAwait(false);
                await this.BroadcastAsync(e.ExecutionId, wf, c => c.ExecutionCompleted(
                    new ExecutionCompletedEvent(e.ExecutionId, wf, e.Duration.TotalMilliseconds, now))).ConfigureAwait(false);
                break;
            }

            case WorkflowFailed e:
            {
                var wf = await this.ResolveWorkflowIdAsync(e.ExecutionId, ct).ConfigureAwait(false);
                await this.BroadcastAsync(e.ExecutionId, wf, c => c.ExecutionFailed(
                    new ExecutionFailedEvent(e.ExecutionId, wf, e.Error?.Message ?? "Execution failed", e.Duration.TotalMilliseconds, now))).ConfigureAwait(false);
                break;
            }

            case NodeStateChanged e when e.NewState == NodeExecutionState.Running:
            {
                var wf = await this.ResolveWorkflowIdAsync(e.ExecutionId, ct).ConfigureAwait(false);
                await this.BroadcastAsync(e.ExecutionId, wf, c => c.NodeStarted(
                    new NodeStartedEvent(e.ExecutionId, e.NodeId, e.Timestamp))).ConfigureAwait(false);
                break;
            }

            case NodeExecutionCompleted e:
            {
                var wf = await this.ResolveWorkflowIdAsync(e.ExecutionId, ct).ConfigureAwait(false);
                await this.BroadcastAsync(e.ExecutionId, wf, c => c.NodeCompleted(
                    new NodeCompletedEvent(e.ExecutionId, e.NodeId, e.Duration.TotalMilliseconds, now))).ConfigureAwait(false);
                break;
            }

            case NodeExecutionFailed e:
            {
                var wf = await this.ResolveWorkflowIdAsync(e.ExecutionId, ct).ConfigureAwait(false);
                await this.BroadcastAsync(e.ExecutionId, wf, c => c.NodeFailed(
                    new NodeFailedEvent(e.ExecutionId, e.NodeId, e.Error?.Message ?? "Node failed", e.Duration.TotalMilliseconds, now))).ConfigureAwait(false);
                break;
            }

            case ProgressUpdate e:
            {
                string? current = null;
                e.CurrentNode.IfSome(x => current = x);
                var wf = await this.ResolveWorkflowIdAsync(e.ExecutionId, ct).ConfigureAwait(false);
                await this.BroadcastAsync(e.ExecutionId, wf, c => c.ExecutionProgress(
                    new ExecutionProgressEvent(e.ExecutionId, e.Percentage, current, e.CompletedNodes, e.TotalNodes, now))).ConfigureAwait(false);
                break;
            }

            default:
                // Non-start state transitions (terminal states arrive via Workflow*/NodeExecution* events)~
                break;
        }
    }

    private Task BroadcastAsync(Guid executionId, Guid? workflowId, Func<IWorkflowHubClient, Task> send)
    {
        var groups = new List<string>(3) { RealTimeGroups.Execution(executionId), RealTimeGroups.All };
        if (workflowId is Guid wf)
        {
            groups.Add(RealTimeGroups.Workflow(wf));
        }

        // Clients.Groups(list) delivers once per connection even if it belongs to several groups~
        return send(this.hub.Clients.Groups(groups));
    }

    private async Task<Guid?> ResolveWorkflowIdAsync(Guid executionId, CancellationToken ct)
    {
        if (this.executionToWorkflow.TryGetValue(executionId, out var cached))
        {
            return cached;
        }

        var history = this.services.GetService(typeof(IExecutionHistoryRepository)) as IExecutionHistoryRepository;
        if (history is null)
        {
            return null;
        }

        try
        {
            var record = await history.GetExecutionAsync(executionId, ct).ConfigureAwait(false);
            if (record is not null)
            {
                this.executionToWorkflow[executionId] = record.WorkflowId;
                return record.WorkflowId;
            }
        }
        catch (Exception ex)
        {
            this.logger.LogDebug(ex, "📡 Could not resolve workflow id for execution {ExecutionId}~", executionId);
        }

        return null;
    }

    /// <summary>
    /// 📡 A tiny Akka actor that forwards subscribed <c>EventStream</c> messages into the bridge's
    /// channel for off-thread processing~ ✨.
    /// </summary>
    private sealed class EventForwarder : UntypedActor
    {
        private readonly ChannelWriter<object> writer;

        public EventForwarder(ChannelWriter<object> writer) => this.writer = writer;

        protected override void OnReceive(object message) => this.writer.TryWrite(message);
    }
}
