// <copyright file="ActorWorkflowExecutionService.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Execution;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Workflow.Api.Observability;
using Workflow.Api.Webhooks;
using Workflow.Core.Models;
using Workflow.Engine.Messages;
using Workflow.Persistence.Abstractions;

/// <summary>
/// 🚀 Default <see cref="IWorkflowExecutionService"/> — dispatches through the Akka
/// <c>WorkflowSupervisor</c> and falls back to persisted history for terminal executions (2.7.2)~ ✨.
/// </summary>
public sealed class ActorWorkflowExecutionService : IWorkflowExecutionService
{
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(15);

    private readonly WorkflowSupervisorActorRef supervisor;
    private readonly IWorkflowRepository workflows;
    private readonly IExecutionHistoryRepository history;
    private readonly IWorkflowMetrics metrics;
    private readonly ILogger<ActorWorkflowExecutionService> logger;

    /// <summary>Initializes a new instance of the <see cref="ActorWorkflowExecutionService"/> class~ 🚀.</summary>
    /// <param name="supervisor">The supervisor actor ref.</param>
    /// <param name="workflows">The workflow repository.</param>
    /// <param name="history">The execution history repository.</param>
    /// <param name="metrics">The execution metrics seam.</param>
    /// <param name="logger">The logger.</param>
    public ActorWorkflowExecutionService(
        WorkflowSupervisorActorRef supervisor,
        IWorkflowRepository workflows,
        IExecutionHistoryRepository history,
        IWorkflowMetrics metrics,
        ILogger<ActorWorkflowExecutionService> logger)
    {
        this.supervisor = supervisor;
        this.workflows = workflows;
        this.history = history;
        this.metrics = metrics;
        this.logger = logger;
    }

    /// <inheritdoc/>
    public async Task<StartResult> StartAsync(
        Guid definitionId,
        IReadOnlyDictionary<string, object?> inputs,
        ExecutionStartOptions options,
        CancellationToken ct = default)
    {
        var definition = await this.workflows.GetByIdAsync(definitionId, ct).ConfigureAwait(false);
        if (definition is null)
        {
            return new StartResult(false, Guid.Empty, new[] { $"Workflow '{definitionId}' was not found." });
        }

        var inputMap = inputs.Aggregate(
            HashMap<string, object?>.Empty,
            (acc, kv) => acc.Add(kv.Key, kv.Value));

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(AskTimeout);

        try
        {
            var response = await this.supervisor.ActorRef
                .Ask<IWorkflowMessage>(
                    new CreateWorkflowInstance(definition.Id, definition, inputMap, options),
                    cancellationToken: linkedCts.Token)
                .ConfigureAwait(false);

            return response switch
            {
                WorkflowInstanceCreated created => this.OnStarted(created.ExecutionId),
                WorkflowInstanceCreationFailed failed => new StartResult(false, Guid.Empty, failed.Errors.ToArray()),
                _ => new StartResult(false, Guid.Empty, new[] { $"Unexpected supervisor response: {response.GetType().Name}" }),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            this.logger.LogError(ex, "🚀 Failed to start workflow {WorkflowId}", definitionId);
            return new StartResult(false, Guid.Empty, new[] { $"Failed to start execution: {ex.Message}" });
        }
    }

    /// <inheritdoc/>
    public async Task<ExecutionStatusResult?> GetStatusAsync(Guid executionId, CancellationToken ct = default)
    {
        // 1️⃣ Try the live actor status~
        using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            linkedCts.CancelAfter(AskTimeout);
            try
            {
                var status = await this.supervisor.ActorRef
                    .Ask<WorkflowStatusResponse>(new GetWorkflowStatus(executionId), cancellationToken: linkedCts.Token)
                    .ConfigureAwait(false);

                return FromLiveStatus(status);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Unknown/terminal execution (supervisor replied Status.Failure) — fall through to history~
                this.logger.LogDebug("📊 Live status unavailable for {ExecutionId}, trying history~", executionId);
            }
        }

        // 2️⃣ Fall back to persisted history~
        var record = await this.history.GetExecutionAsync(executionId, ct).ConfigureAwait(false);
        return record is null ? null : FromHistory(record);
    }

    /// <inheritdoc/>
    public async Task<bool> CancelAsync(Guid executionId, CancellationToken ct = default)
    {
        var status = await this.GetStatusAsync(executionId, ct).ConfigureAwait(false);
        if (status is null)
        {
            return false;
        }

        // Fire-and-forget cancel through the supervisor (the executor doesn't reply to Ask)~ 🛑
        this.supervisor.ActorRef.Tell(new CancelExecution(executionId));
        return true;
    }

    /// <inheritdoc/>
    public async Task<(StartResult Start, ExecutionStatusResult? Final, bool TimedOut)> StartAndWaitAsync(
        Guid definitionId,
        IReadOnlyDictionary<string, object?> inputs,
        ExecutionStartOptions options,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        var start = await this.StartAsync(definitionId, inputs, options, ct).ConfigureAwait(false);
        if (!start.Success)
        {
            return (start, null, false);
        }

        var deadline = DateTimeOffset.UtcNow + timeout;
        var delay = TimeSpan.FromMilliseconds(50);

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var status = await this.GetStatusAsync(start.ExecutionId, ct).ConfigureAwait(false);
            if (status is not null && IsTerminal(status.State))
            {
                this.RecordTerminal(status.State);
                return (start, status, false);
            }

            await Task.Delay(delay, ct).ConfigureAwait(false);
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 500));
        }

        var last = await this.GetStatusAsync(start.ExecutionId, ct).ConfigureAwait(false);
        if (last is not null && IsTerminal(last.State))
        {
            this.RecordTerminal(last.State);
            return (start, last, false);
        }

        return (start, last, true);
    }

    private static bool IsTerminal(ExecutionState state)
        => state is ExecutionState.Completed or ExecutionState.Failed or ExecutionState.Cancelled;

    private StartResult OnStarted(Guid executionId)
    {
        this.metrics.RecordStarted();
        return new StartResult(true, executionId, Array.Empty<string>());
    }

    private void RecordTerminal(ExecutionState state)
    {
        switch (state)
        {
            case ExecutionState.Completed:
                this.metrics.RecordCompleted();
                break;
            case ExecutionState.Failed:
                this.metrics.RecordFailed();
                break;
            case ExecutionState.Cancelled:
                this.metrics.RecordCancelled();
                break;
        }
    }

    private static ExecutionStatusResult FromLiveStatus(WorkflowStatusResponse s)
    {
        var nodeStates = new Dictionary<string, string>();
        foreach (var kv in s.NodeStates)
        {
            nodeStates[kv.Key] = kv.Value.ToString();
        }

        return new ExecutionStatusResult(
            s.ExecutionId,
            s.State,
            s.Progress,
            nodeStates,
            s.StartTime,
            s.EndTime.IsSome ? s.EndTime.IfNone(default(DateTimeOffset)) : null,
            s.Error.IsSome ? s.Error.IfNone(string.Empty) : null,
            null);
    }

    private static ExecutionStatusResult FromHistory(Workflow.Persistence.Models.ExecutionRecord r)
        => new(
            r.ExecutionId,
            r.State,
            r.State is ExecutionState.Completed ? 100 : 0,
            new Dictionary<string, string>(),
            r.StartedAt,
            r.CompletedAt,
            r.Error,
            r.Outputs);
}
