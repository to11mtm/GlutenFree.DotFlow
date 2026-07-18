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
            .Select(ModuleSummaryDto.From)
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

    private static IResult GetHandler(HttpContext http, string moduleId)
    {
        var registry = http.RequestServices.GetService<IModuleRegistry>();
        if (registry is null)
        {
            return ApiResults.ServiceUnavailableProblem("No module registry is configured.");
        }

        var module = registry.GetModule(moduleId);
        return module is null
            ? ApiResults.NotFoundProblem($"Module '{moduleId}' was not found.")
            : Results.Ok(ModuleDetailsDto.From(module));
    }
}
