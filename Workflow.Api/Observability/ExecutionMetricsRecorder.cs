// <copyright file="ExecutionMetricsRecorder.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Observability;

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Workflow.Engine.Messages;

/// <summary>
/// 📈 §9.3 — records every run's terminal outcome on <see cref="IWorkflowMetrics"/>, whatever started
/// it (async API, sync API, webhook). It listens to the terminal events the executor publishes on the
/// Akka <c>EventStream</c> (§12.1), so there's one place that sees "a run finished". Before this,
/// only the wait-for-result path recorded outcomes, so <c>executions_active</c> never dropped for
/// async/webhook runs and the hot-reload drain gate reported activity forever~ ✨.
/// </summary>
/// <remarks>
/// "Started" is still recorded where a run is submitted (the execution service and the webhook
/// launcher), because that's known synchronously. Each submitted run publishes exactly one terminal
/// event: <see cref="WorkflowCompleted"/>, or <see cref="WorkflowFailed"/> — a user cancel arrives as
/// <see cref="WorkflowFailed"/> with an <see cref="OperationCanceledException"/>. Paused runs stay active.
/// </remarks>
public sealed class ExecutionMetricsRecorder : IHostedService
{
    private readonly IServiceProvider services;
    private readonly IWorkflowMetrics metrics;
    private ActorSystem? system;
    private IActorRef? listener;

    /// <summary>Initializes a new instance of the <see cref="ExecutionMetricsRecorder"/> class.</summary>
    /// <param name="services">The root service provider (the actor system is resolved at start).</param>
    /// <param name="metrics">The metrics to record into.</param>
    public ExecutionMetricsRecorder(IServiceProvider services, IWorkflowMetrics metrics)
    {
        this.services = services;
        this.metrics = metrics;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        this.system = this.services.GetRequiredService<ActorSystem>();
        this.listener = this.system.ActorOf(Props.Create(() => new TerminalListener(this.metrics)), "execution-metrics-recorder");
        this.system.EventStream.Subscribe(this.listener, typeof(WorkflowCompleted));
        this.system.EventStream.Subscribe(this.listener, typeof(WorkflowFailed));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Idempotent, and safe when the actor system already shut down first (hosted services stop in
        // reverse registration order, so the Akka host may be gone by now) — its actors are gone too~
        var listener = Interlocked.Exchange(ref this.listener, null);
        if (this.system is null || listener is null || this.system.WhenTerminated.IsCompleted)
        {
            return Task.CompletedTask;
        }

        this.system.EventStream.Unsubscribe(listener);
        this.system.Stop(listener);
        return Task.CompletedTask;
    }

    /// <summary>Maps terminal events to metric outcomes (counters are thread-safe).</summary>
    private sealed class TerminalListener : ReceiveActor
    {
        public TerminalListener(IWorkflowMetrics metrics)
        {
            this.Receive<WorkflowCompleted>(_ => metrics.RecordCompleted());
            this.Receive<WorkflowFailed>(m =>
            {
                if (m.Error is OperationCanceledException)
                {
                    metrics.RecordCancelled();
                }
                else
                {
                    metrics.RecordFailed();
                }
            });
        }
    }
}
