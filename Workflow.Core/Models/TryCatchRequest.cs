// <copyright file="TryCatchRequest.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Core.Models;

/// <summary>
/// 🛡️ Request returned by <c>builtin.trycatch</c> so the engine can set up an error containment
/// zone and orchestrate try → catch? → finally? execution sequentially~ ✨💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.2.4 — analogous to <see cref="LoopRequest"/> (2.2.2) and
/// <see cref="ParallelRequest"/> (2.2.3a). <c>WorkflowExecutor</c> detects a non-null
/// <c>ModuleResult.TryCatch</c> after <c>NodeExecutionCompleted</c> and spawns a
/// <c>TryCatchExecutorActor</c> to orchestrate the phases~ 🌸.
/// </para>
/// <para>
/// Port names default to their canonical values (<c>"try"</c>, <c>"catch"</c>,
/// <c>"finally"</c>, <c>"done"</c>) but are configurable for multi-trycatch patterns~
/// </para>
/// </remarks>
public sealed class TryCatchRequest
{
    /// <summary>
    /// Gets or sets the output port name whose connections form the try-body sub-graph~
    /// Default: <c>"try"</c>~ 🛡️
    /// </summary>
    public string TryPort { get; init; } = "try";

    /// <summary>
    /// Gets or sets the output port name whose connections form the catch-body sub-graph~
    /// Default: <c>"catch"</c>~ 🪤
    /// </summary>
    public string CatchPort { get; init; } = "catch";

    /// <summary>
    /// Gets or sets the output port name whose connections form the finally-body sub-graph~
    /// Default: <c>"finally"</c>~ 🧹
    /// </summary>
    public string FinallyPort { get; init; } = "finally";

    /// <summary>
    /// Gets or sets the output port name fired after the entire try/catch/finally completes~
    /// Default: <c>"done"</c>~ ✅
    /// </summary>
    public string DonePort { get; init; } = "done";

    /// <summary>
    /// Gets or sets a value indicating whether to re-raise the caught error after the finally block~
    /// When <see langword="true"/> the workflow fails after finally completes; otherwise the
    /// workflow continues normally~ ❗
    /// Default: <see langword="false"/>~ 💖
    /// </summary>
    public bool Rethrow { get; init; }

    /// <summary>
    /// Gets or sets the error type names this boundary should catch~
    /// <see langword="null"/> or empty = catch-all (any exception)~ 🎣
    /// </summary>
    public string[]? CatchTypes { get; init; }
}

