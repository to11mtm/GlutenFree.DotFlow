// <copyright file="TransactionExecutorActor.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Actors;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;
using Workflow.Core.Models;
using Workflow.Engine.Messages;
using Workflow.Modules.Abstractions;

/// <summary>
/// 💼 Orchestrates a database transaction body for a <c>builtin.database.transaction</c> node~
/// Spawned by <c>WorkflowExecutor</c> when a node completion carries a
/// <see cref="TransactionRequest"/>. Reports <see cref="TransactionCompleted"/> or
/// <see cref="TransactionFailed"/> to its parent when the sequence ends~ ✨💖
/// </summary>
public sealed class TransactionExecutorActor : ReceiveActor
{
    private const string SubGraphBody = "transactionBody";

    private readonly string _transactionNodeId;
    private readonly TransactionRequest _request;
    private readonly WorkflowDefinition _definition;
    private readonly Guid _executionId;
    private readonly IServiceProvider _serviceProvider;
    private readonly CancellationTokenSource _linkedCts;
    private readonly ILoggingAdapter _log;
    private readonly Stopwatch _timer = new();

    private IWorkflowTransactionScope? _scope;
    private IAmbientDbTransactions? _ambient;
    private bool _ambientRegistered;

    private sealed record TransactionOpened(IWorkflowTransactionScope? Scope, bool Transactional);

    private sealed record TransactionOpenFailed(Exception Error);

    private sealed record TransactionCommitted;

    private sealed record TransactionCommitFailed(Exception Error);

    private sealed record TransactionRolledBack(Exception Error, string? FailedNodeId);

    private sealed record TransactionRollbackFailed(Exception Error);

    /// <summary>
    /// Initializes a new <see cref="TransactionExecutorActor"/>~ 💼✨
    /// </summary>
    public TransactionExecutorActor(
        string transactionNodeId,
        TransactionRequest request,
        WorkflowDefinition definition,
        Guid executionId,
        IServiceProvider serviceProvider,
        CancellationToken parentToken = default)
    {
        _transactionNodeId = transactionNodeId;
        _request = request;
        _definition = definition;
        _executionId = executionId;
        _serviceProvider = serviceProvider;
        _log = Context.GetLogger();
        _linkedCts = parentToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(parentToken)
            : new CancellationTokenSource();

        Receive<TransactionOpened>(HandleTransactionOpened);
        Receive<TransactionOpenFailed>(m => FailInfrastructure(m.Error));
        Receive<SubGraphCompleted>(HandleSubGraphCompleted);
        Receive<SubGraphFailed>(HandleSubGraphFailed);
        Receive<TransactionCommitted>(_ => CompleteCommitted());
        Receive<TransactionCommitFailed>(m => FailInfrastructure(m.Error));
        Receive<TransactionRolledBack>(m => CompleteRolledBack(m.Error, m.FailedNodeId));
        Receive<TransactionRollbackFailed>(m => FailInfrastructure(m.Error));
        Receive<Terminated>(_ => { });
    }

    /// <summary>Creates Props for spawning a <see cref="TransactionExecutorActor"/>~ 🏭✨</summary>
    public static Props Props(
        string transactionNodeId,
        TransactionRequest request,
        WorkflowDefinition definition,
        Guid executionId,
        IServiceProvider serviceProvider,
        CancellationToken parentToken = default)
    {
        return Akka.Actor.Props.Create(() => new TransactionExecutorActor(
            transactionNodeId, request, definition, executionId, serviceProvider, parentToken));
    }

    /// <inheritdoc/>
    protected override void PreStart()
    {
        base.PreStart();
        _timer.Start();

        _log.Info(
            "💼 TransactionExecutorActor starting for node '{TransactionNodeId}' under execution {ExecutionId}",
            _transactionNodeId, _executionId);

        var entryNodes = FindEntryNodes(_request.BodyPort);
        if (entryNodes.Count == 0)
        {
            _log.Warning(
                "⚠️ Transaction '{TransactionNodeId}': no connections via '{BodyPort}' port — nothing to do",
                _transactionNodeId, _request.BodyPort);
            _timer.Stop();
            Context.Parent.Tell(new TransactionCompleted(_transactionNodeId, new Dictionary<string, object?>
            {
                ["success"] = true,
                ["committed"] = false,
                ["rolledBack"] = false,
                ["durationMs"] = 0L,
            }));
            Context.Stop(Self);
            return;
        }

        OpenTransactionAsync().PipeTo(
            Self,
            success: opened => opened,
            failure: ex => new TransactionOpenFailed(ex));
    }

    /// <inheritdoc/>
    protected override void PostStop()
    {
        _linkedCts.Dispose();
        base.PostStop();
    }

    /// <summary>
    /// BFS from entryNodeIds within the workflow graph, stopping at nodes that loop back to the owner~
    /// CopilotNote: mirrors <c>TryCatchExecutorActor.ComputeScope</c>~ 🛡️
    /// </summary>
    public static IReadOnlyCollection<string> ComputeScope(
        WorkflowDefinition definition,
        string ownerNodeId,
        IReadOnlyList<string> entryNodeIds)
    {
        if (entryNodeIds.Count == 0) return Array.Empty<string>();

        var scope = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(entryNodeIds);

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            if (!scope.Add(nodeId)) continue;

            foreach (var conn in definition.Connections)
            {
                if (conn.SourceNodeId != nodeId) continue;
                if (conn.TargetNodeId == ownerNodeId) continue;
                if (!scope.Contains(conn.TargetNodeId))
                {
                    queue.Enqueue(conn.TargetNodeId);
                }
            }
        }

        return scope;
    }

    private async Task<TransactionOpened> OpenTransactionAsync()
    {
        _ambient = _serviceProvider.GetService(typeof(IAmbientDbTransactions)) as IAmbientDbTransactions;
        var factory = _serviceProvider.GetService(typeof(IWorkflowTransactionScopeFactory)) as IWorkflowTransactionScopeFactory;

        if (factory is null || _ambient is null)
        {
            _log.Warning(
                "⚠️ Transaction '{TransactionNodeId}': transaction services are not registered — running body without an open transaction~",
                _transactionNodeId);
            return new TransactionOpened(null, Transactional: false);
        }

        if (!string.IsNullOrWhiteSpace(_request.ConnectionId)
            && _ambient.TryGet(_executionId, _request.ConnectionId!) is not null)
        {
            _log.Warning(
                "⚠️ Transaction '{TransactionNodeId}': nested transaction for connection '{ConnectionId}' is not supported — running body non-structurally~",
                _transactionNodeId, _request.ConnectionId);
            return new TransactionOpened(null, Transactional: false);
        }

        IWorkflowTransactionScope scope;
        if (!string.IsNullOrWhiteSpace(_request.ConnectionId))
        {
            scope = await factory.OpenAsync(
                _request.ConnectionId!, _request.IsolationLevel, _request.TimeoutSeconds, _linkedCts.Token).ConfigureAwait(false);
        }
        else if (!string.IsNullOrWhiteSpace(_request.ConnectionString) && !string.IsNullOrWhiteSpace(_request.Provider))
        {
            scope = await factory.OpenAsync(
                _request.Provider!, _request.ConnectionString!, _request.IsolationLevel, _request.TimeoutSeconds, _linkedCts.Token).ConfigureAwait(false);
        }
        else
        {
            _log.Warning(
                "⚠️ Transaction '{TransactionNodeId}': no usable connection configured — running body without an open transaction~",
                _transactionNodeId);
            return new TransactionOpened(null, Transactional: false);
        }

        return new TransactionOpened(scope, Transactional: true);
    }

    private void HandleTransactionOpened(TransactionOpened message)
    {
        _scope = message.Scope;

        if (_scope is not null && !string.IsNullOrWhiteSpace(_request.ConnectionId) && _ambient is not null)
        {
            _ambient.Register(_executionId, _request.ConnectionId!, _scope.DataConnection);
            _ambientRegistered = true;
        }

        var entryNodes = FindEntryNodes(_request.BodyPort);
        var scope = ComputeScope(_definition, _transactionNodeId, entryNodes);
        SpawnSubGraph(entryNodes, scope);
    }

    private void HandleSubGraphCompleted(SubGraphCompleted message)
    {
        _log.Info("✅ Transaction '{TransactionNodeId}': body completed; committing~", _transactionNodeId);

        if (_scope is null)
        {
            CompleteCommitted();
            return;
        }

        CommitAndCleanupAsync().PipeTo(
            Self,
            success: _ => new TransactionCommitted(),
            failure: ex => new TransactionCommitFailed(ex));
    }

    private void HandleSubGraphFailed(SubGraphFailed message)
    {
        _log.Warning(
            "⚡ Transaction '{TransactionNodeId}': body failed; rolling back — {Error}",
            _transactionNodeId, message.Error.Message);

        if (_scope is null)
        {
            CompleteRolledBack(message.Error, message.FailedNodeId);
            return;
        }

        RollbackAndCleanupAsync(message.Error, message.FailedNodeId).PipeTo(
            Self,
            success: rolledBack => rolledBack,
            failure: ex => new TransactionRollbackFailed(ex));
    }

    private async Task<bool> CommitAndCleanupAsync()
    {
        await _scope!.CommitAsync(_linkedCts.Token).ConfigureAwait(false);
        await CleanupScopeAsync().ConfigureAwait(false);
        return true;
    }

    private async Task<TransactionRolledBack> RollbackAndCleanupAsync(Exception error, string? failedNodeId)
    {
        await _scope!.RollbackAsync(_linkedCts.Token).ConfigureAwait(false);
        await CleanupScopeAsync().ConfigureAwait(false);
        return new TransactionRolledBack(error, failedNodeId);
    }

    private async Task CleanupScopeAsync()
    {
        if (_ambientRegistered && _ambient is not null && !string.IsNullOrWhiteSpace(_request.ConnectionId))
        {
            _ambient.Remove(_executionId, _request.ConnectionId!);
            _ambientRegistered = false;
        }

        if (_scope is not null)
        {
            await _scope.DisposeAsync().ConfigureAwait(false);
            _scope = null;
        }
    }

    private void CompleteCommitted()
    {
        _timer.Stop();
        Context.Parent.Tell(new TransactionCompleted(_transactionNodeId, new Dictionary<string, object?>
        {
            ["success"] = true,
            ["committed"] = true,
            ["rolledBack"] = false,
            ["durationMs"] = _timer.ElapsedMilliseconds,
        }));
        Context.Stop(Self);
    }

    private void CompleteRolledBack(Exception error, string? failedNodeId)
    {
        _timer.Stop();
        Context.Parent.Tell(new TransactionCompleted(_transactionNodeId, new Dictionary<string, object?>
        {
            ["success"] = false,
            ["committed"] = false,
            ["rolledBack"] = true,
            ["error"] = WorkflowError.FromException(error, failedNodeId),
            ["durationMs"] = _timer.ElapsedMilliseconds,
        }));
        Context.Stop(Self);
    }

    private void FailInfrastructure(Exception error)
    {
        _timer.Stop();
        _log.Error(error, "❌ Transaction '{TransactionNodeId}' infrastructure failure: {Error}", _transactionNodeId, error.Message);
        CleanupScopeAsync().PipeTo(Self, success: () => new TransactionFailed(_transactionNodeId, error), failure: _ => new TransactionFailed(_transactionNodeId, error));
        Become(() => Receive<TransactionFailed>(m => { Context.Parent.Tell(m); Context.Stop(Self); }));
    }

    private List<string> FindEntryNodes(string portName)
    {
        return _definition.Connections
            .Where(c => c.SourceNodeId == _transactionNodeId && c.SourcePortName == portName)
            .Select(c => c.TargetNodeId)
            .ToList();
    }

    private void SpawnSubGraph(List<string> entryNodes, IReadOnlyCollection<string> scope)
    {
        var actorName = $"txn-body-{_transactionNodeId.Replace(".", "-")}";
        var child = Context.ActorOf(
            SubGraphExecutor.Props(
                _executionId,
                _definition,
                scope,
                entryNodes,
                new Dictionary<string, object?>(),
                _serviceProvider,
                subGraphId: $"{SubGraphBody}:{_transactionNodeId}",
                parentToken: _linkedCts.Token),
            actorName);

        Context.Watch(child);

        _log.Info(
            "💼 Transaction '{TransactionNodeId}': spawned body sub-graph '{ActorName}' ({EntryCount} entry nodes, {ScopeCount} scope nodes)",
            _transactionNodeId, actorName, entryNodes.Count, scope.Count);
    }
}
