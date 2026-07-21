// <copyright file="IWorkflowHubClient.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.RealTime;

using System.Threading.Tasks;
using Workflow.Api.Contracts.RealTime;

/// <summary>
/// 📡 Phase 3.2 — The strongly-typed SignalR client contract for <see cref="WorkflowHub"/>. Each
/// method is a message a connected client can subscribe to (e.g. JS
/// <c>connection.on("ExecutionStarted", ...)</c>). Using a typed hub client makes broadcasts
/// compile-safe~ ✨.
/// </summary>
public interface IWorkflowHubClient
{
    /// <summary>Pushed when an execution starts running~ 🚀.</summary>
    /// <param name="e">The event payload.</param>
    /// <returns>A task.</returns>
    Task ExecutionStarted(ExecutionStartedEvent e);

    /// <summary>Pushed when an execution completes successfully~ 🎊.</summary>
    /// <param name="e">The event payload.</param>
    /// <returns>A task.</returns>
    Task ExecutionCompleted(ExecutionCompletedEvent e);

    /// <summary>Pushed when an execution fails~ 😿.</summary>
    /// <param name="e">The event payload.</param>
    /// <returns>A task.</returns>
    Task ExecutionFailed(ExecutionFailedEvent e);

    /// <summary>Pushed when a node starts executing~ ⚡.</summary>
    /// <param name="e">The event payload.</param>
    /// <returns>A task.</returns>
    Task NodeStarted(NodeStartedEvent e);

    /// <summary>Pushed when a node completes~ ✅.</summary>
    /// <param name="e">The event payload.</param>
    /// <returns>A task.</returns>
    Task NodeCompleted(NodeCompletedEvent e);

    /// <summary>Pushed when a node fails~ ⚠️.</summary>
    /// <param name="e">The event payload.</param>
    /// <returns>A task.</returns>
    Task NodeFailed(NodeFailedEvent e);

    /// <summary>Pushed on execution progress updates~ 💫.</summary>
    /// <param name="e">The event payload.</param>
    /// <returns>A task.</returns>
    Task ExecutionProgress(ExecutionProgressEvent e);

    /// <summary>Pushed to the caller right after subscribing to an execution (initial snapshot)~ 📸.</summary>
    /// <param name="e">The snapshot payload.</param>
    /// <returns>A task.</returns>
    Task ExecutionSnapshot(ExecutionSnapshotEvent e);
}
