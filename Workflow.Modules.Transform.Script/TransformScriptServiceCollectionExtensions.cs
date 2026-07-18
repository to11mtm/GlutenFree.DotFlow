// <copyright file="TransformScriptServiceCollectionExtensions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Transform.Script;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Transform.Script.Abstractions;
using Workflow.Modules.Transform.Script.Builtin;
using Workflow.Modules.Transform.Script.Compilation;
using Workflow.Scripting.Roslyn;
using Workflow.Scripting.Roslyn.Abstractions;
using Workflow.Scripting.Roslyn.Execution;
using Workflow.Persistence.Abstractions;

/// <summary>
/// 🌟 DI registration for the transform-script family (Roslyn quarantined here, D4)~ ✨💖.
/// </summary>
/// <remarks>
/// CopilotNote: Host-wired (not <c>AddWorkflowModules()</c>) — this project references the Roslyn
/// scripting core, so it must never be pulled transitively into <c>Workflow.Modules</c>. Requires an
/// <c>IBlobStore</c> registration for the compiled-assembly cache~ 🌸.
/// </remarks>
public static class TransformScriptServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared scripting core, the transform compiler + cache, and the script module~ 🌟.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining~ 💖.</returns>
    public static IServiceCollection AddTransformScriptModules(this IServiceCollection services)
    {
        services.AddScriptingCore();

        services.TryAddSingleton<ITransformScriptCompiler, TransformScriptCompiler>();
        services.TryAddSingleton<ITransformScriptPreviewer, TransformScriptPreviewer>();

        // The compiled-assembly cache needs an IBlobStore (host-registered)~ 📦
        services.TryAddSingleton<ICompiledScriptCache>(sp => new CompiledScriptCache(
            sp.GetRequiredService<IBlobStore>(),
            sp.GetRequiredService<IScriptAssemblySigner>()));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowModule, TransformScriptModule>());

        return services;
    }
}
