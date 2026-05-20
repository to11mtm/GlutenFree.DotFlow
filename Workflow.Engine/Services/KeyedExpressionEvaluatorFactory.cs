// <copyright file="KeyedExpressionEvaluatorFactory.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Services;

using System;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Core.Abstractions;

/// <summary>
/// 🔌 Resolves the appropriate <see cref="IExpressionEvaluator"/> by engine name~
/// Default: <c>"javascript"</c> → <see cref="JintExpressionEvaluator"/>.
/// Opt-in: <c>"csharp"</c> → <see cref="DynamicExpressoEvaluator"/>~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.2.5 — register with DI via:
/// <code>
/// services.AddSingleton&lt;IExpressionEvaluator, JintExpressionEvaluator&gt;();  // default
/// services.AddKeyedSingleton&lt;IExpressionEvaluator, DynamicExpressoEvaluator&gt;("csharp");
/// services.AddSingleton&lt;IExpressionEvaluatorFactory, KeyedExpressionEvaluatorFactory&gt;();
/// </code>
/// Authors can opt in to the C# evaluator by setting <c>engineName: "csharp"</c> in
/// <c>WorkflowDefinition.Metadata</c>~ 🔌
/// </remarks>
public interface IExpressionEvaluatorFactory
{
    /// <summary>
    /// Gets the expression evaluator for the specified engine name~
    /// </summary>
    /// <param name="engineName">The engine name (e.g. <c>"javascript"</c>, <c>"csharp"</c>). Null → default.</param>
    /// <returns>The registered <see cref="IExpressionEvaluator"/> for that engine.</returns>
    IExpressionEvaluator GetEvaluator(string? engineName = null);
}

/// <summary>
/// 🔌 Implementation of <see cref="IExpressionEvaluatorFactory"/> using <see cref="IServiceProvider"/>~ ✨
/// </summary>
public sealed class KeyedExpressionEvaluatorFactory : IExpressionEvaluatorFactory
{
    private readonly IServiceProvider _services;
    private readonly IExpressionEvaluator _default;

    /// <summary>Initializes a new instance of <see cref="KeyedExpressionEvaluatorFactory"/>~ 💉.</summary>
    /// <param name="services">DI service provider for keyed lookups.</param>
    /// <param name="defaultEvaluator">The primary evaluator (Jint).</param>
    public KeyedExpressionEvaluatorFactory(
        IServiceProvider services,
        IExpressionEvaluator defaultEvaluator)
    {
        _services = services;
        _default = defaultEvaluator;
    }

    /// <inheritdoc/>
    public IExpressionEvaluator GetEvaluator(string? engineName = null)
    {
        if (string.IsNullOrWhiteSpace(engineName)
            || string.Equals(engineName, "javascript", StringComparison.OrdinalIgnoreCase)
            || string.Equals(engineName, "js", StringComparison.OrdinalIgnoreCase))
        {
            return _default;
        }

        // Try keyed DI lookup for opt-in engines (e.g. "csharp")~ 🔌
        var keyed = _services.GetKeyedService<IExpressionEvaluator>(engineName);
        if (keyed is not null)
        {
            return keyed;
        }

        // Fall back to default rather than throwing — graceful degradation~ 🛡️
        return _default;
    }
}

