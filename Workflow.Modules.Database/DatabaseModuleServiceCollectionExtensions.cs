// <copyright file="DatabaseModuleServiceCollectionExtensions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Builtin;
using Workflow.Modules.Database.Catalog;
using Workflow.Modules.Database.Configuration;
using Workflow.Modules.Database.Connections;
using Workflow.Modules.Database.Providers;

/// <summary>
/// 🗄️✨ DI registration entry point for the database built-in module family~ 💖.
/// </summary>
/// <remarks>
/// <para>
/// Call <see cref="AddDatabaseModules"/> once in your host startup
/// (e.g. <c>builder.Services.AddDatabaseModules()</c> in <c>Program.cs</c>).
/// Bind <see cref="DatabaseConnectionsOptions"/> from configuration BEFORE calling this,
/// or use the <c>services.Configure&lt;DatabaseConnectionsOptions&gt;(config.GetSection(...))</c>
/// pattern — the in-memory registry hydrates from the bound options at first resolve~ ⚙️.
/// </para>
/// <para>
/// CopilotNote: Phase 2.4.a.0 — ⚠️ unlike the HTTP family, this extension CANNOT be called
/// from <c>Workflow.Modules</c>' <c>AddWorkflowModules()</c>: <c>Workflow.Modules.Database</c>
/// references <c>Workflow.Modules</c> (for <c>IWorkflowModule</c>), so the reverse call would be
/// circular. The HOST (Workflow.Api) wires this family explicitly — same pattern D14 prescribes
/// for the linq family (2.4.b). All registrations use TryAdd so hosts can pre-register overrides
/// (e.g. a plugin's <see cref="IDbProviderRegistry"/> with more providers — D6)~ 🌸.
/// </para>
/// </remarks>
public static class DatabaseModuleServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared database infrastructure (provider registry, connection registry,
    /// connection factory, table catalog) and — as they land in 2.4.a.1–4 — the four
    /// built-in database modules~ 🗄️✨.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining~ 💖.</returns>
    public static IServiceCollection AddDatabaseModules(this IServiceCollection services)
    {
        // ⚙️ Options plumbing — ensures IOptions<DatabaseConnectionsOptions> resolves even
        // when the host didn't bind a config section (empty registry, still functional)~
        services.AddOptions<DatabaseConnectionsOptions>();

        // 🗂️ Provider-key → linq2db ProviderName mapping (postgres/sqlite in V1 — D5/D6)~
        services.TryAddSingleton<IDbProviderRegistry, DefaultDbProviderRegistry>();

        // 📇 Named connections — in-memory, config-hydrated. 2.4.a.5 overrides with the
        // persisted registry when IPersistenceProvider.DbConnections is available~
        services.TryAddSingleton<IDbConnectionRegistry, InMemoryDbConnectionRegistry>();

        // 🔌 The single connection seam both module families share (D2)~
        services.TryAddSingleton<IDbConnectionFactory, DefaultDbConnectionFactory>();

        // 📚 Table catalog stub — manual registration only in V1 (Q4/D10)~
        services.TryAddSingleton<IWorkflowTableCatalog, InMemoryWorkflowTableCatalog>();

        // 🔍 2.4.a.1 — Database Query module (SELECT-only). Registered as an enumerable
        //    IWorkflowModule so hosts that resolve IEnumerable<IWorkflowModule> from DI pick it up;
        //    reflection-based ModuleDiscovery also finds it via assembly scan (host wiring in 2.4.a.5).
        //    2.4.a.2–4 append execute ✏️ · transaction 💼 · bulkinsert 📊 here as they land~
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowModule, DatabaseQueryModule>());

        return services;
    }
}


