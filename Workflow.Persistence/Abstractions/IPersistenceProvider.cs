// <copyright file="IPersistenceProvider.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Abstractions;

using Workflow.Persistence.Models;

/// <summary>
///  Top-level persistence provider interface. Each provider (Postgres, NATS, S3, In-Memory)
/// implements this to manage lifecycle, health, and expose its sub-repositories~ ✨
/// </summary>
/// <remarks>
/// CopilotNote: Providers are composable via <c>CompositePersistenceProvider</c> — you can route
/// workflows to Postgres and variables to NATS in the same deployment! UwU
/// </remarks>
public interface IPersistenceProvider : IAsyncDisposable
{
    /// <summary>Gets the provider name (e.g. <c>"postgres"</c>, <c>"nats"</c>, <c>"memory"</c>)~ ️.</summary>
    string ProviderName { get; }

    /// <summary>Gets whether <see cref="InitializeAsync"/> has been called successfully~ ✅.</summary>
    bool IsInitialized { get; }

    /// <summary>Gets the workflow definition repository provided by this provider~ .</summary>
    IWorkflowRepository Workflows { get; }

    /// <summary>Gets the execution history repository provided by this provider~ .</summary>
    IExecutionHistoryRepository ExecutionHistory { get; }

    /// <summary>Gets the variable store provided by this provider~ .</summary>
    IVariableStore Variables { get; }

    /// <summary>Gets the blob store provided by this provider (may be null if unsupported)~ ️.</summary>
    IBlobStore? Blobs { get; }

    /// <summary>
    /// Gets the webhook registration repository provided by this provider, or <c>null</c> if unsupported~ .
    /// </summary>
    /// <remarks>
    /// CopilotNote: Phase 2.3.9 — only <see cref="Workflow.Persistence.Sqlite.SqlitePersistenceProvider"/>
    /// implements this in V1. When <c>null</c>, the API falls back to
    /// <see cref="InMemoryWebhookRegistrationRepository"/> which loses state on restart~
    /// </remarks>
    IWebhookRegistrationRepository? Webhooks { get; }

    /// <summary>
    /// Initializes the provider: runs migrations, creates buckets, verifies connectivity~ .
    /// Must be called before using any repository.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Performs a health check against the underlying storage~ .
    /// </summary>
    Task<HealthCheckResult> HealthCheckAsync(CancellationToken ct = default);
}

