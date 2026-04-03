// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using LanguageExt;
using Workflow.Engine.Models;

namespace Workflow.Engine.Services;

/// <summary>
/// Abstraction for persisting and retrieving workflow execution state snapshots~ 💾✨
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: This interface decouples state persistence from the actor system!
/// The in-memory implementation is great for testing, but swap it out for a
/// real database-backed implementation in production (Phase 2). The interface
/// supports async operations for non-blocking I/O, nya~ 💖
/// </para>
/// <para>
/// Future implementations could use:
/// - Akka.Persistence event sourcing
/// - PostgreSQL / SQLite
/// - Redis for high-performance caching
/// - Azure Blob Storage for durable snapshots
/// </para>
/// </remarks>
public interface IExecutionStateStore
{
    /// <summary>
    /// Saves a snapshot of the execution context~ 💾
    /// </summary>
    /// <param name="context">The execution context to persist.</param>
    /// <param name="cancellationToken">Cancellation token for async operation.</param>
    /// <returns>Task completing when the snapshot is saved. ✅</returns>
    Task SaveSnapshotAsync(
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a previously saved execution snapshot~ 📥
    /// </summary>
    /// <param name="executionId">The execution ID to look up.</param>
    /// <param name="cancellationToken">Cancellation token for async operation.</param>
    /// <returns>Some(context) if found, None if no snapshot exists. 🔍</returns>
    Task<Option<WorkflowExecutionContext>> LoadSnapshotAsync(
        Guid executionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a saved execution snapshot (cleanup after completion)~ 🗑️
    /// </summary>
    /// <param name="executionId">The execution ID to delete.</param>
    /// <param name="cancellationToken">Cancellation token for async operation.</param>
    /// <returns>True if a snapshot was deleted, false if none existed. ✨</returns>
    Task<bool> DeleteSnapshotAsync(
        Guid executionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all saved execution snapshot IDs~ 📋
    /// Useful for monitoring and recovery scenarios.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operation.</param>
    /// <returns>Collection of execution IDs with saved snapshots. 🗂️</returns>
    Task<IReadOnlyList<Guid>> ListSnapshotsAsync(
        CancellationToken cancellationToken = default);
}

