// <copyright file="ScriptExecutionContext.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Abstractions;

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

/// <summary>
/// 📦 Phase 3.1 — Everything a script executor needs for one run: inputs, the variable snapshot, the
/// gated <c>workflow</c> API, sandbox config, and workflow identity~ ✨.
/// </summary>
public sealed record ScriptExecutionContext
{
    /// <summary>Gets the script inputs (the <c>input</c> global).</summary>
    public required IReadOnlyDictionary<string, object?> Inputs { get; init; }

    /// <summary>Gets the read-only variable snapshot the script may read.</summary>
    public required IReadOnlyDictionary<string, object?> Variables { get; init; }

    /// <summary>Gets the gated workflow API object exposed as <c>workflow</c>.</summary>
    public required IWorkflowScriptApi Api { get; init; }

    /// <summary>Gets the sandbox config (already clamped to host ceilings).</summary>
    public required ScriptExecutionConfig Config { get; init; }

    /// <summary>Gets the current execution id.</summary>
    public Guid ExecutionId { get; init; }

    /// <summary>Gets the current workflow id.</summary>
    public Guid WorkflowId { get; init; }

    /// <summary>Gets the current node id.</summary>
    public string NodeId { get; init; } = string.Empty;

    /// <summary>Gets the logger for the executor.</summary>
    public required ILogger Logger { get; init; }

    /// <summary>Gets an optional list of pre-resolved script libraries to inject before the body (3.1.5).</summary>
    public IReadOnlyList<ScriptLibrarySource> Libraries { get; init; } = Array.Empty<ScriptLibrarySource>();
}

/// <summary>
/// 📚 Phase 3.1 — A resolved script library's code + id, ready for an executor to inject (3.1.5)~ ✨.
/// </summary>
/// <param name="LibraryId">The library id used for imports.</param>
/// <param name="Code">The library source code (same language as the script).</param>
public sealed record ScriptLibrarySource(string LibraryId, string Code);
