// <copyright file="CompositePersistenceProvider.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Composite;

using System.Diagnostics;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;

/// <summary>
///  A persistence provider that routes each repository interface to a configured sub-provider.
/// Enables mixing providers (e.g. Postgres for workflows + NATS for variables)~ ✨
/// </summary>
/// <remarks>
/// CopilotNote: This provider delegates ALL operations to its sub-providers.
/// It does NOT implement any storage logic itself — it's purely a router~ UwU
/// </remarks>
public sealed class CompositePersistenceProvider : IPersistenceProvider
{
    private readonly IPersistenceProvider _workflowsProvider;
    private readonly IPersistenceProvider _executionHistoryProvider;
    private readonly IPersistenceProvider _variablesProvider;
    private readonly IPersistenceProvider? _blobsProvider;
    private readonly HashSet<IPersistenceProvider> _uniqueProviders;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositePersistenceProvider"/> class~ .
    /// </summary>
    /// <param name="workflowsProvider">Provider for <see cref="IWorkflowRepository"/>.</param>
    /// <param name="executionHistoryProvider">Provider for <see cref="IExecutionHistoryRepository"/>.</param>
    /// <param name="variablesProvider">Provider for <see cref="IVariableStore"/>.</param>
    /// <param name="blobsProvider">Optional provider for <see cref="IBlobStore"/>.</param>
    public CompositePersistenceProvider(
        IPersistenceProvider workflowsProvider,
        IPersistenceProvider executionHistoryProvider,
        IPersistenceProvider variablesProvider,
        IPersistenceProvider? blobsProvider = null)
    {
        _workflowsProvider = workflowsProvider ?? throw new ArgumentNullException(nameof(workflowsProvider));
        _executionHistoryProvider = executionHistoryProvider ?? throw new ArgumentNullException(nameof(executionHistoryProvider));
        _variablesProvider = variablesProvider ?? throw new ArgumentNullException(nameof(variablesProvider));
        _blobsProvider = blobsProvider;

        // Track unique provider instances (avoid double-init/dispose if same provider used for multiple roles)
        _uniqueProviders = new HashSet<IPersistenceProvider>(ReferenceEqualityComparer.Instance)
        {
            _workflowsProvider,
            _executionHistoryProvider,
            _variablesProvider,
        };
        if (_blobsProvider != null)
        {
            _uniqueProviders.Add(_blobsProvider);
        }
    }

    /// <inheritdoc />
    public string ProviderName => "composite";

    /// <inheritdoc />
    public bool IsInitialized => _uniqueProviders.All(p => p.IsInitialized);

    /// <inheritdoc />
    public IWorkflowRepository Workflows => _workflowsProvider.Workflows;

    /// <inheritdoc />
    public IExecutionHistoryRepository ExecutionHistory => _executionHistoryProvider.ExecutionHistory;

    /// <inheritdoc />
    public IVariableStore Variables => _variablesProvider.Variables;

    /// <inheritdoc />
    public IBlobStore? Blobs => _blobsProvider?.Blobs;

    /// <inheritdoc />
    /// <remarks>
    /// CopilotNote: Composite provider returns <c>null</c> for Webhooks — the webhook repo is
    /// resolved from the first sub-provider that supports it (future enhancement).
    /// For now, webhooks are registered via the API-layer DI fallback to InMemory~
    /// </remarks>
    public IWebhookRegistrationRepository? Webhooks =>
        _uniqueProviders.Select(p => p.Webhooks).FirstOrDefault(w => w is not null);

    /// <inheritdoc />
    /// <remarks>Initialises all unique sub-providers in parallel~ .</remarks>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await Task.WhenAll(_uniqueProviders.Select(p => p.InitializeAsync(ct))).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>Aggregates health from all unique sub-providers~ .</remarks>
    public async Task<HealthCheckResult> HealthCheckAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var results = await Task.WhenAll(_uniqueProviders.Select(p => p.HealthCheckAsync(ct))).ConfigureAwait(false);
        sw.Stop();

        var allHealthy = results.All(r => r.IsHealthy);
        var details = new Dictionary<string, object?>();
        foreach (var r in results)
        {
            details[$"{r.ProviderName}.isHealthy"] = r.IsHealthy;
            details[$"{r.ProviderName}.latencyMs"] = r.Latency.TotalMilliseconds;
            if (r.ErrorMessage != null)
            {
                details[$"{r.ProviderName}.error"] = r.ErrorMessage;
            }
        }

        return new HealthCheckResult(
            IsHealthy: allHealthy,
            ProviderName: "composite",
            Latency: sw.Elapsed,
            ErrorMessage: allHealthy ? null : "One or more sub-providers are unhealthy~ ",
            Details: details);
    }

    /// <inheritdoc />
    /// <remarks>Disposes all unique sub-providers~ .</remarks>
    public async ValueTask DisposeAsync()
    {
        foreach (var provider in _uniqueProviders)
        {
            await provider.DisposeAsync().ConfigureAwait(false);
        }
    }
}

