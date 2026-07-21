// <copyright file="BreakModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Flow;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// ⏹️ Built-in loop break module (<c>builtin.break</c>)~
/// When executed inside a loop body, signals the enclosing loop to stop all further iterations.
/// Returns the <c>__loop_break__</c> sentinel output detected by <c>SubGraphExecutor</c>~ ✨💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.2.2 — BreakModule returns <see cref="ModuleResult.Break()"/> which
/// writes the sentinel output key <c>__loop_break__</c>. SubGraphExecutor propagates this flag
/// to the parent <c>LoopExecutorActor</c> via <see cref="Workflow.Engine.Messages.SubGraphCompleted.BreakRequested"/>~ 🌸.
/// </para>
/// <para>
/// Using BreakModule outside a loop body will silently succeed — the sentinel key is only
/// meaningful when processed by a LoopExecutorActor. At runtime, unhandled break is a no-op
/// (the sentinel key is filtered from all non-loop outputs)~ 💡.
/// </para>
/// </remarks>
public sealed class BreakModule : IWorkflowModule
{
    /// <inheritdoc/>
    public string ModuleId => "builtin.break";

    /// <inheritdoc/>
    public string DisplayName => "Break";

    /// <inheritdoc/>
    public string Category => "Flow Control";

    /// <inheritdoc/>
    public string Description => "Stops the current loop — no further iterations~ ⏹️";

    /// <inheritdoc/>
    public string Icon => "⏹️";

    /// <inheritdoc/>
    public Version Version => new(1, 0, 0);

    /// <inheritdoc/>
    public ModuleSchema Schema { get; } = new(
        Inputs: Arr<PortDefinition>.Empty,
        Outputs: Arr<PortDefinition>.Empty,
        Properties: Arr<ModulePropertyDefinition>.Empty);

    /// <inheritdoc/>
    public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
        => Task.FromResult(ModuleResult.Break());
}

