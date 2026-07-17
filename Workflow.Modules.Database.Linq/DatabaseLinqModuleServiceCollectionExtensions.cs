// <copyright file="DatabaseLinqModuleServiceCollectionExtensions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Database.Linq.Abstractions;
using Workflow.Modules.Database.Linq.Builtin;
using Workflow.Modules.Database.Linq.Compilation;
using Workflow.Modules.Database.Linq.Execution;

/// <summary>
/// 🧬✨ Opt-in DI registration entry point for the typed linq family (<c>builtin.database.linq</c>)~ 💖.
/// </summary>
/// <remarks>
/// <para>
/// Call <see cref="AddDatabaseLinqModules"/> explicitly in your host startup — it is deliberately
/// <b>separate</b> from <c>AddWorkflowModules()</c> and <c>AddDatabaseModules()</c> so that hosts which
/// only want the raw-SQL escape-hatch family (or no DB at all) never load Roslyn
/// (<c>Microsoft.CodeAnalysis.*</c>, ~30MB transitive). This is the D14 quarantine — the whole reason
/// the linq family lives in its own assembly~ 🌸.
/// </para>
/// <para>
/// CopilotNote: Phase 2.4.b.0 is scaffolding only — this method currently wires nothing beyond being
/// chainable/idempotent. The real registrations land as their slices ship:
/// <list type="bullet">
///   <item><description>2.4.b.1 — <c>IWorkflowLinqCompiler</c> (Roslyn compile pipeline + whitelists)</description></item>
///   <item><description>2.4.b.2 — compiled-assembly cache over <c>IBlobStore</c></description></item>
///   <item><description>2.4.b.3 — <c>LinqQueryModule</c> (<c>builtin.database.linq</c>) + collectible ALC executor</description></item>
///   <item><description>2.4.b.4 — <c>IWorkflowLinqPreviewer</c> (rollback-only SQLite sandbox)</description></item>
/// </list>
/// All registrations will use <c>TryAdd*</c> so hosts/plugins can override~ 💡.
/// </para>
/// </remarks>
public static class DatabaseLinqModuleServiceCollectionExtensions
{
    /// <summary>
    /// Registers the typed linq family (compiler, previewer, and the <c>builtin.database.linq</c>
    /// module) as they land in 2.4.b.1–4. Scaffolding no-op today; safe to call now~ 🧬.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining~ 💖.</returns>
    public static IServiceCollection AddDatabaseLinqModules(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 🧬 2.4.b.1 — the Roslyn compile pipeline (dual-POCO: plugin OR column-generated)~
        services.TryAddSingleton<TableTypeResolver>();
        services.TryAddSingleton<IWorkflowLinqCompiler, WorkflowLinqCompiler>();

        // 📦 2.4.b.2 — compiled-assembly cache (IBlobStore + HMAC + LRU). The host may replace the
        //    ephemeral HMAC key with a Data-Protection-backed stable key for cross-restart cache reuse~
        services.AddOptions<LinqCompileCacheOptions>();
        services.TryAddSingleton<ILinqHmacKeyProvider, EphemeralLinqHmacKeyProvider>();
        services.TryAddSingleton<ILinqAssemblySigner, HmacLinqAssemblySigner>();
        services.TryAddSingleton<ICompiledAssemblyCache, CompiledAssemblyCache>();

        // 🚀 2.4.b.3 — collectible-ALC runner + the builtin.database.linq module (opt-in, D14)~
        services.TryAddSingleton<ILinqScriptRunner, CollectibleScriptRunner>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowModule, LinqQueryModule>());

        // 🧩 2.4.b.4 registration (previewer) slots in here (TryAdd so hosts can override)~
        return services;
    }
}

