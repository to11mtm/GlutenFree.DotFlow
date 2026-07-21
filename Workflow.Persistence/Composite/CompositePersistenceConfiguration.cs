// <copyright file="CompositePersistenceConfiguration.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Composite;

/// <summary>
/// 🔀 Configuration for a composite persistence provider that routes each interface
/// to a different sub-provider (e.g. Postgres for workflows + NATS for variables)~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: If a sub-config is not set, it falls back to <see cref="WorkflowsProvider"/>.
/// This means a minimal config only needs <c>WorkflowsProvider</c> set — everything else
/// defaults to the same provider, just like the single-provider mode~ UwU 💖
/// </remarks>
public record CompositePersistenceConfiguration
{
    /// <summary>Gets the provider configuration for <c>IWorkflowRepository</c>. Required~ 📋.</summary>
    public required PersistenceConfiguration WorkflowsProvider { get; init; }

    /// <summary>Gets the provider configuration for <c>IExecutionHistoryRepository</c>.
    /// Falls back to <see cref="WorkflowsProvider"/> if null~ 📊.</summary>
    public PersistenceConfiguration? ExecutionHistoryProvider { get; init; }

    /// <summary>Gets the provider configuration for <c>IVariableStore</c>.
    /// Falls back to <see cref="WorkflowsProvider"/> if null~ 💾.</summary>
    public PersistenceConfiguration? VariablesProvider { get; init; }

    /// <summary>Gets the provider configuration for <c>IBlobStore</c>.
    /// Falls back to <see cref="WorkflowsProvider"/> if null~ 🗃️.</summary>
    public PersistenceConfiguration? BlobsProvider { get; init; }

    /// <summary>Gets the effective provider config for execution history~ 📊.</summary>
    public PersistenceConfiguration EffectiveExecutionHistoryProvider =>
        ExecutionHistoryProvider ?? WorkflowsProvider;

    /// <summary>Gets the effective provider config for variables~ 💾.</summary>
    public PersistenceConfiguration EffectiveVariablesProvider =>
        VariablesProvider ?? WorkflowsProvider;

    /// <summary>Gets the effective provider config for blobs~ 🗃️.</summary>
    public PersistenceConfiguration EffectiveBlobsProvider =>
        BlobsProvider ?? WorkflowsProvider;
}

