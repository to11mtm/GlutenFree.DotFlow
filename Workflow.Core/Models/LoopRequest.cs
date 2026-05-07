// <copyright file="LoopRequest.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Core.Models;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 🔁 Packages the parameters for a loop execution request returned by a loop module~
/// Detected by <c>WorkflowExecutor</c> in <c>NodeExecutionCompleted</c>; it spawns a
/// <c>LoopExecutorActor</c> to drive all iterations. Modules stay declarative~ ✨💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.2.2 — returned via
/// <see cref="Workflow.Modules.Abstractions.ModuleResult.WithLoop"/>.
/// The engine owns all iteration orchestration; the module just packages the spec.
/// </para>
/// <para>
/// <see cref="ContinueCondition"/> is a local-only <c>Func</c> delegate (not serialisable).
/// It is used by <c>WhileModule</c> to re-evaluate the loop condition before each iteration,
/// called with the merged variable context from the previous iteration's outputs~ 🔄.
/// </para>
/// </remarks>
public sealed class LoopRequest
{
    /// <summary>
    /// Gets the items to iterate over. <see langword="null"/> for condition-driven loops
    /// (<c>WhileModule</c>) — they use <see cref="ContinueCondition"/> instead~ 🎁.
    /// </summary>
    public IReadOnlyList<object?>? Items { get; init; }

    /// <summary>
    /// Gets the output port name for the loop body activation (typically <c>"loopBody"</c>)~ 🔁.
    /// Connections from this port reach the first nodes of the sub-graph body.
    /// </summary>
    public required string LoopBodyPort { get; init; }

    /// <summary>
    /// Gets the output port name fired after all iterations complete (typically <c>"done"</c>)~ ✅.
    /// </summary>
    public required string DonePort { get; init; }

    /// <summary>
    /// Gets the maximum number of iterations before the loop fails with <c>LoopLimitExceeded</c>.
    /// Default is 1000~ 🔢.
    /// </summary>
    public int MaxIterations { get; init; } = 1000;

    /// <summary>
    /// Gets whether per-iteration errors are collected and execution continues (default <see langword="false"/>).
    /// When <see langword="false"/>, the first sub-graph failure bubbles up immediately~ 🛡️.
    /// </summary>
    public bool ContinueOnError { get; init; } = false;

    /// <summary>
    /// Gets optional condition re-evaluated before each iteration (used by <c>WhileModule</c>).
    /// Called with the current merged variable/output context from the previous iteration.
    /// When <see langword="null"/>, the loop iterates over <see cref="Items"/> until exhausted or break~ 🔄.
    /// </summary>
    /// <remarks>
    /// CopilotNote: This is a <c>Func</c> delegate — NOT MessagePack-serialisable.
    /// Only used in-process (local actor system). For clustering, this field would need special handling~ 💡.
    /// </remarks>
    public Func<IReadOnlyDictionary<string, object?>, CancellationToken, ValueTask<bool>>? ContinueCondition { get; init; }
}

