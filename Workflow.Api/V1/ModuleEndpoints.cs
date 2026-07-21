// <copyright file="ModuleEndpoints.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.V1;

using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Api.Auth;
using Workflow.Api.Contracts.Modules;
using Workflow.Modules.Abstractions;

/// <summary>
/// 📦 Phase 2.7.3 — Read-only module discovery endpoints over the DI-registered
/// <see cref="IModuleRegistry"/>~ ✨💖. Upload/enable/disable land in Phase 2.8 with the
/// <c>.wfmod</c> package format (Q4).
/// </summary>
public static class ModuleEndpoints
{
    /// <summary>Registers the <c>/api/v1/modules</c> endpoints~ 📦.</summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapModuleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapV1Group().MapGroup("/modules").WithTags("Modules");

        group.MapGet("/", ListHandler).WithName("ListModules").RequireAuthorization(AuthConstants.WorkflowReadPolicy);
        group.MapGet("/{moduleId}", GetHandler).WithName("GetModule").RequireAuthorization(AuthConstants.WorkflowReadPolicy);

        return app;
    }

    private static IResult ListHandler(
        HttpContext http,
        string? category,
        string? q,
        bool? groupByCategory)
    {
        var registry = http.RequestServices.GetService<IModuleRegistry>();
        if (registry is null)
        {
            return ApiResults.ServiceUnavailableProblem("No module registry is configured.");
        }

        IReadOnlyList<IWorkflowModule> modules;
        if (!string.IsNullOrWhiteSpace(q))
        {
            modules = registry.SearchModules(q);
        }
        else if (!string.IsNullOrWhiteSpace(category))
        {
            modules = registry.GetModulesByCategory(category);
        }
        else
        {
            modules = registry.GetAllModules();
        }

        var summaries = modules
            .OrderBy(m => m.Category)
            .ThenBy(m => m.ModuleId)
            .Select(m => ModuleSummaryDto.From(m, registry.IsModuleEnabled(m.ModuleId, m.Version)))
            .ToList();

        if (groupByCategory == true)
        {
            var grouped = summaries
                .GroupBy(s => s.Category)
                .ToDictionary(g => g.Key, g => g.ToList());
            return Results.Ok(grouped);
        }

        return Results.Ok(summaries);
    }

    private static IResult GetHandler(HttpContext http, string moduleId, string? version)
    {
        var registry = http.RequestServices.GetService<IModuleRegistry>();
        if (registry is null)
        {
            return ApiResults.ServiceUnavailableProblem("No module registry is configured.");
        }

        var versions = registry.GetModuleVersions(moduleId).Select(v => v.ToString()).ToList();

        IWorkflowModule? module;
        bool enabled;
        if (!string.IsNullOrWhiteSpace(version) && System.Version.TryParse(version, out var pinned))
        {
            module = registry.GetModule(moduleId, pinned);
            enabled = module is not null && registry.IsModuleEnabled(moduleId, pinned);
        }
        else
        {
            module = registry.GetModule(moduleId);
            enabled = module is not null && registry.IsModuleEnabled(moduleId, module.Version);
        }

        return module is null
            ? ApiResults.NotFoundProblem($"Module '{moduleId}'{(version is null ? string.Empty : $" v{version}")} was not found.")
            : Results.Ok(ModuleDetailsDto.From(module, enabled, versions));
    }
}
