// <copyright file="IScriptExecutor.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Abstractions;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 📜 Phase 3.1 — A language-specific, sandboxed script executor. Registered keyed by
/// <see cref="LanguageId"/> and resolved via <see cref="IScriptExecutorFactory"/> (D1)~ ✨.
/// </summary>
public interface IScriptExecutor
{
    /// <summary>Gets the language id this executor handles (e.g. <c>"javascript"</c>, <c>"lua"</c>, <c>"csharp"</c>).</summary>
    string LanguageId { get; }

    /// <summary>Gets the human-readable display name (e.g. <c>"JavaScript"</c>).</summary>
    string DisplayName { get; }

    /// <summary>Executes a script in the sandbox~ 📜.</summary>
    /// <param name="code">The script source.</param>
    /// <param name="context">The execution context (inputs, variables, api, config).</param>
    /// <param name="ct">Cancellation token (also drives the timeout).</param>
    /// <returns>The structured execution result.</returns>
    Task<ScriptExecutionResult> ExecuteAsync(
        string code,
        ScriptExecutionContext context,
        CancellationToken ct = default);
}

/// <summary>
/// 🏭 Phase 3.1 — Resolves script executors by language id and lists the registered languages (D1)~ ✨.
/// </summary>
public interface IScriptExecutorFactory
{
    /// <summary>Gets the executor for a language, or <c>null</c> when unregistered~ 🏭.</summary>
    /// <param name="languageId">The language id (case-insensitive).</param>
    /// <returns>The executor, or <c>null</c>.</returns>
    IScriptExecutor? GetExecutor(string languageId);

    /// <summary>Gets the registered languages (id + display name)~ 📋.</summary>
    /// <returns>The registered languages.</returns>
    IReadOnlyList<ScriptLanguageInfo> GetRegisteredLanguages();
}

/// <summary>
/// 📋 Phase 3.1 — Metadata for a registered script language~ ✨.
/// </summary>
/// <param name="LanguageId">The language id.</param>
/// <param name="DisplayName">The display name.</param>
public sealed record ScriptLanguageInfo(string LanguageId, string DisplayName);
