// <copyright file="ExecutorLifecycleOptions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Models;

using System;

/// <summary>
/// How long a finished <see cref="Workflow.Engine.Actors.WorkflowExecutor"/> stays alive (§12.2).
/// Register an instance in DI to override the defaults; when none is registered the defaults apply.
/// </summary>
/// <remarks>
/// After a run reaches Completed/Failed/Cancelled, the executor stops once its persistence writes
/// are acknowledged AND <see cref="TerminalGracePeriod"/> has passed. During the grace period
/// status queries still get the rich live answer (node states, progress); afterwards they fall back
/// to execution history. Paused runs are never stopped.
/// </remarks>
public sealed record ExecutorLifecycleOptions
{
    /// <summary>Gets the defaults used when no options are registered.</summary>
    public static ExecutorLifecycleOptions Default { get; } = new();

    /// <summary>
    /// Gets how long a finished executor keeps answering live status before it stops.
    /// <see cref="TimeSpan.Zero"/> stops it as soon as persistence drains.
    /// </summary>
    public TimeSpan TerminalGracePeriod { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets how long after the grace period to wait for outstanding persistence acks before
    /// stopping anyway (a hung write must not keep the actor alive forever).
    /// </summary>
    public TimeSpan PersistenceAckTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
