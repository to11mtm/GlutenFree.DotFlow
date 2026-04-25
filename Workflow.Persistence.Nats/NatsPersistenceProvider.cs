// <copyright file="NatsPersistenceProvider.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Nats;

using System.Diagnostics;
using NATS.Client.KeyValueStore;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;
using Workflow.Persistence.Nats.Repositories;

/// <summary>
/// 🚀 NATS JetStream KV-backed persistence provider~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: All repositories are backed by NATS KV buckets created during <see cref="InitializeAsync"/>.
/// Blobs are not supported — returns <c>null</c> for <see cref="Blobs"/>; use S3 provider for blob storage.
/// The provider manages one <see cref="NatsConnectionManager"/> for the lifetime of the application~ 🔗
/// </remarks>
public sealed class NatsPersistenceProvider : IPersistenceProvider
{
    private readonly NatsConnectionManager _connectionManager;
    private bool _initialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="NatsPersistenceProvider"/> class~ 🔌.
    /// </summary>
    /// <param name="natsUrl">NATS server URL (e.g. <c>nats://localhost:4222</c>).</param>
    public NatsPersistenceProvider(string natsUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(natsUrl);
        NatsUrl = natsUrl;
        _connectionManager = new NatsConnectionManager(natsUrl);

        // CopilotNote: Repositories are initialised with real stores in InitializeAsync.
        // Setting to null! here because proper stores require async bucket creation~ 🧠
        Workflows = null!;
        ExecutionHistory = null!;
        Variables = null!;
    }

    /// <inheritdoc/>
    public string ProviderName => "nats";

    /// <inheritdoc/>
    public bool IsInitialized => _initialized;

    /// <inheritdoc/>
    public IWorkflowRepository Workflows { get; private set; }

    /// <inheritdoc/>
    public IExecutionHistoryRepository ExecutionHistory { get; private set; }

    /// <inheritdoc/>
    public IVariableStore Variables { get; private set; }

    /// <inheritdoc/>
    /// <remarks>NATS KV is not suitable for large binary blobs — use S3 provider instead~ ☁️.</remarks>
    public IBlobStore? Blobs => null;

    /// <summary>Gets the NATS server URL~ 🔗.</summary>
    public string NatsUrl { get; }

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var kv = _connectionManager.KV;

        // CopilotNote: CreateOrUpdateStoreAsync is idempotent — creates if missing, returns existing if present~ ✅
        var workflowStore = await kv.CreateOrUpdateStoreAsync(
            new NatsKVConfig(NatsWorkflowRepository.BucketName)
            {
                // No history needed for workflow documents (we store IsActive flag instead of revisions)
                History = 1,
            },
            ct).ConfigureAwait(false);

        var execStore = await kv.CreateOrUpdateStoreAsync(
            new NatsKVConfig(NatsExecutionHistoryRepository.ExecutionsBucket)
            {
                History = 1,
            },
            ct).ConfigureAwait(false);

        var nodeStore = await kv.CreateOrUpdateStoreAsync(
            new NatsKVConfig(NatsExecutionHistoryRepository.NodesBucket)
            {
                History = 1,
            },
            ct).ConfigureAwait(false);

        var variableStore = await kv.CreateOrUpdateStoreAsync(
            new NatsKVConfig(NatsVariableStore.BucketName)
            {
                // CopilotNote: NATS KV hard max is 64. Use 64 to retain as many versions as possible~ 💾
                History = 64,
            },
            ct).ConfigureAwait(false);

        Workflows = new NatsWorkflowRepository(workflowStore);
        ExecutionHistory = new NatsExecutionHistoryRepository(execStore, nodeStore);
        Variables = new NatsVariableStore(variableStore);

        _initialized = true;
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> HealthCheckAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var latency = await _connectionManager.PingAsync(ct).ConfigureAwait(false);
            sw.Stop();
            return new HealthCheckResult(true, ProviderName, latency);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new HealthCheckResult(false, ProviderName, sw.Elapsed, ex.Message);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
        => await _connectionManager.DisposeAsync().ConfigureAwait(false);
}

