// <copyright file="ScriptingRoslynServiceCollectionExtensions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Roslyn;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Workflow.Scripting.Roslyn.Abstractions;
using Workflow.Scripting.Roslyn.Compilation;
using Workflow.Scripting.Roslyn.Execution;

/// <summary>
/// 🧬 DI registration for the shared, domain-agnostic Roslyn scripting core~ ✨💖.
/// </summary>
/// <remarks>
/// CopilotNote: Registers the compiler + HMAC signer (ephemeral key by default) + collectible runner.
/// The compiled-assembly cache needs an <c>IBlobStore</c>, so it is registered by the consuming host
/// (which knows which blob store to use). Consumers wire their own module family on top~ 🌸.
/// </remarks>
public static class ScriptingRoslynServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared scripting-core services~ 🧬.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining~ 💖.</returns>
    public static IServiceCollection AddScriptingCore(this IServiceCollection services)
    {
        services.TryAddSingleton<IRoslynScriptCompiler, RoslynScriptCompiler>();
        services.TryAddSingleton<IScriptHmacKeyProvider, EphemeralScriptHmacKeyProvider>();
        services.TryAddSingleton<IScriptAssemblySigner, HmacScriptAssemblySigner>();
        services.TryAddSingleton<CollectibleScriptRunner>();
        return services;
    }
}
