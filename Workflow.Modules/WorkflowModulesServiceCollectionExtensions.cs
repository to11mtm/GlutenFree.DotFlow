// <copyright file="WorkflowModulesServiceCollectionExtensions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules;

using Microsoft.Extensions.DependencyInjection;
using Workflow.Modules.Builtin.File;
using Workflow.Modules.Builtin.Http;

/// <summary>
/// 🗂️✨ Top-level DI registration entry point for the entire <c>Workflow.Modules</c> layer~ 💖.
/// </summary>
/// <remarks>
/// <para>
/// This is the "smart layer" — call <see cref="AddWorkflowModules"/> once in your host
/// startup (e.g. <c>builder.Services.AddWorkflowModules()</c> in <c>Program.cs</c>) and it
/// will aggregate every built-in module family's DI registrations in one go~ 🌸
/// </para>
/// <para>
/// As new module families land (HTTP today, Database in 2.4, Messaging later, …) they get
/// added here so hosts never need to know about family-specific extension methods. Individual
/// family extensions (like <see cref="HttpModuleServiceCollectionExtensions.AddHttpModules"/>)
/// remain public for advanced hosts that want fine-grained control over which families are wired up~ 🧠
/// </para>
/// <para>
/// CopilotNote: This file lives at the root of <c>Workflow.Modules</c> on purpose — that's the
/// natural top-level entry point. Don't push it down into a sub-namespace or hosts will have to
/// add an extra <c>using</c>~ 🌷
/// </para>
/// </remarks>
public static class WorkflowModulesServiceCollectionExtensions
{
    /// <summary>
    /// Registers every built-in workflow module family's DI services in a single call~ 🗂️💖.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining~ ✨.</returns>
    /// <remarks>
    /// Currently aggregates:
    /// <list type="bullet">
    ///   <item><description>Phase 2.3 — <see cref="HttpModuleServiceCollectionExtensions.AddHttpModules"/> (HTTP family, <c>IHttpClientFactory</c> named client)</description></item>
    /// </list>
    /// Future families append here as they land~ 🌸
    /// </remarks>
    public static IServiceCollection AddWorkflowModules(this IServiceCollection services)
    {
        // 🌐 Phase 2.3 — HTTP request/webhook family (IHttpClientFactory named "dotflow.http")~
        services.AddHttpModules();

        // 📁 Phase 2.5 — file-system family (path-security sandbox + IWorkflowPathValidator)~
        services.AddFileSystemModules();

        // 🔮 Future families plug in here (e.g. services.AddDatabaseModules(), services.AddMessagingModules())~

        return services;
    }
}

