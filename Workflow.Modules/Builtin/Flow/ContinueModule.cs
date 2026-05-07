// <copyright file="ContinueModule.cs" company="GlutenFree">
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
/// ⏭️ Built-in loop continue module (<c>builtin.continue</c>)~
/// When executed inside a loop body, skips the remainder of the current iteration and
/// advances to the next. Returns the <c>__loop_continue__</c> sentinel output~ ✨💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.2.2 — ContinueModule returns <see cref="ModuleResult.Continue()"/> which
/// writes the sentinel output key <c>__loop_continue__</c>. SubGraphExecutor propagates this flag
/// to the parent <c>LoopExecutorActor</c> via
/// <see cref="Workflow.Engine.Messages.SubGraphCompleted.ContinueRequested"/>~ 🌸.
/// </para>
/// <para>
/// Like <see cref="BreakModule"/>, using ContinueModule outside a loop body is a no-op at runtime.
/// The sentinel key is filtered from non-loop outputs by LoopExecutorActor~ 💡.
/// </para>
/// </remarks>
public sealed class ContinueModule : IWorkflowModule
{
    /// <inheritdoc/>
    public string ModuleId => "builtin.continue";

    /// <inheritdoc/>
    public string DisplayName => "Continue";

    /// <inheritdoc/>
    public string Category => "Flow Control";

    /// <inheritdoc/>
    public string Description => "Skips the rest of the current loop iteration~ ⏭️";

    /// <inheritdoc/>
    public string Icon => "⏭️";

    /// <inheritdoc/>
    public Version Version => new(1, 0, 0);

    /// <inheritdoc/>
    public ModuleSchema Schema { get; } = new(
        Inputs: Arr<PortDefinition>.Empty,
        Outputs: Arr<PortDefinition>.Empty,
        Properties: Arr<ModulePropertyDefinition>.Empty);

    /// <inheritdoc/>
    public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
        => Task.FromResult(ModuleResult.Continue());
}

