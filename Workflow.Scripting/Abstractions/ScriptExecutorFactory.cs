// <copyright file="ScriptExecutorFactory.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Abstractions;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 🏭 Phase 3.1 — Default <see cref="IScriptExecutorFactory"/> backed by the set of registered
/// <see cref="IScriptExecutor"/> instances (all executors are injected via DI)~ ✨.
/// </summary>
public sealed class ScriptExecutorFactory : IScriptExecutorFactory
{
    private readonly Dictionary<string, IScriptExecutor> executors;

    /// <summary>Initializes a new instance of the <see cref="ScriptExecutorFactory"/> class~ 🏭.</summary>
    /// <param name="executors">All registered script executors.</param>
    public ScriptExecutorFactory(IEnumerable<IScriptExecutor> executors)
    {
        ArgumentNullException.ThrowIfNull(executors);
        this.executors = new Dictionary<string, IScriptExecutor>(StringComparer.OrdinalIgnoreCase);
        foreach (var executor in executors)
        {
            this.executors[executor.LanguageId] = executor;
        }
    }

    /// <inheritdoc/>
    public IScriptExecutor? GetExecutor(string languageId)
    {
        if (string.IsNullOrWhiteSpace(languageId))
        {
            return null;
        }

        return this.executors.TryGetValue(languageId, out var executor) ? executor : null;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ScriptLanguageInfo> GetRegisteredLanguages()
        => this.executors.Values
            .Select(e => new ScriptLanguageInfo(e.LanguageId, e.DisplayName))
            .OrderBy(l => l.LanguageId)
            .ToList();
}
