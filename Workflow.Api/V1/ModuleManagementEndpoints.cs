// <copyright file="ModuleManagementEndpoints.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.V1;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Api.Auth;
using Workflow.Api.Contracts.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Dependencies;
using Workflow.Modules.Loading;
using Workflow.Modules.Packaging;

/// <summary>
/// 📦🌐 Phase 2.8.5 — Write-side module management endpoints (upload / enable / disable / uninstall)
/// that complete the read-only Phase 2.7 module API~ ✨💖.
/// </summary>
public static class ModuleManagementEndpoints
{
    /// <summary>Registers the module management endpoints under <c>/api/v1/modules</c>~ 📦.</summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapModuleManagementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapV1Group().MapGroup("/modules").WithTags("Modules");

        group.MapPost("/upload", UploadHandler)
            .WithName("UploadModule")
            .RequireAuthorization(AuthConstants.AdminPolicy)
            .DisableAntiforgery();
        group.MapPost("/{moduleId}/enable", (HttpContext http, string moduleId, string? version, CancellationToken ct)
                => ToggleHandler(http, moduleId, version, enabled: true))
            .WithName("EnableModule")
            .RequireAuthorization(AuthConstants.WorkflowWritePolicy);
        group.MapPost("/{moduleId}/disable", (HttpContext http, string moduleId, string? version, CancellationToken ct)
                => ToggleHandler(http, moduleId, version, enabled: false))
            .WithName("DisableModule")
            .RequireAuthorization(AuthConstants.WorkflowWritePolicy);
        group.MapDelete("/{moduleId}", UninstallHandler)
            .WithName("UninstallModule")
            .RequireAuthorization(AuthConstants.AdminPolicy);

        return app;
    }

    private static async Task<IResult> UploadHandler(HttpContext http, CancellationToken ct)
    {
        var installer = http.RequestServices.GetService<ModulePackageInstaller>();
        var registry = http.RequestServices.GetService<IModuleRegistry>();
        if (installer is null || registry is null)
        {
            return ApiResults.ServiceUnavailableProblem("Module management is not configured.");
        }

        if (!http.Request.HasFormContentType)
        {
            return ApiResults.BadRequestProblem("Expected a multipart/form-data upload with a '.wfmod' file.");
        }

        var form = await http.Request.ReadFormAsync(ct).ConfigureAwait(false);
        var file = form.Files.GetFile("package") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            return ApiResults.BadRequestProblem("No package file was uploaded.");
        }

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, ct).ConfigureAwait(false);
            bytes = ms.ToArray();
        }

        var result = await installer.InstallAsync(bytes, ct).ConfigureAwait(false);
        if (!result.Success)
        {
            return MapInstallFailure(result);
        }

        var module = registry.GetModule(result.ModuleId!, result.Version);
        var versions = registry.GetModuleVersions(result.ModuleId!).Select(v => v.ToString()).ToList();
        var details = module is null
            ? null
            : ModuleDetailsDto.From(module, enabled: true, versions);

        var dto = new ModuleInstallResultDto(details!, result.Warnings);
        return Results.Created($"/api/v1/modules/{result.ModuleId}", dto);
    }

    private static async Task<IResult> ToggleHandler(HttpContext http, string moduleId, string? version, bool enabled)
    {
        var registry = http.RequestServices.GetService<IModuleRegistry>();
        if (registry is null)
        {
            return ApiResults.ServiceUnavailableProblem("No module registry is configured.");
        }

        if (!registry.HasModule(moduleId))
        {
            return ApiResults.NotFoundProblem($"Module '{moduleId}' was not found.");
        }

        var versions = registry.GetModuleVersions(moduleId);
        var targets = versions.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(version))
        {
            if (!Version.TryParse(version, out var pinned))
            {
                return ApiResults.BadRequestProblem($"Invalid version '{version}'.");
            }

            targets = versions.Where(v => v == pinned);
        }

        var affected = targets.Where(v => registry.SetModuleEnabled(moduleId, v, enabled)).Select(v => v.ToString()).ToList();
        if (affected.Count == 0)
        {
            return ApiResults.NotFoundProblem($"No matching version of module '{moduleId}' was found.");
        }

        // 💾 Persist the new enabled state so it survives a restart (Phase 2.8.2)~
        await PersistStateAsync(http, registry).ConfigureAwait(false);

        return Results.Ok(new ModuleToggleResultDto(moduleId, enabled, affected));
    }

    private static async Task PersistStateAsync(HttpContext http, IModuleRegistry registry)
    {
        var stateStore = http.RequestServices.GetService<Workflow.Modules.State.IModuleStateStore>();
        if (stateStore is null)
        {
            return;
        }

        var records = new List<Workflow.Modules.State.ModuleStateRecord>();
        foreach (var module in registry.GetAllModules())
        {
            foreach (var v in registry.GetModuleVersions(module.ModuleId))
            {
                records.Add(new Workflow.Modules.State.ModuleStateRecord(
                    module.ModuleId,
                    v.ToString(),
                    registry.IsModuleEnabled(module.ModuleId, v)));
            }
        }

        try
        {
            await stateStore.SaveAsync(new Workflow.Modules.State.ModuleStateSnapshot(records)).ConfigureAwait(false);
        }
        catch
        {
            // State persistence is best-effort — the in-memory toggle already applied~
        }
    }

    private static async Task<IResult> UninstallHandler(HttpContext http, string moduleId, string? version, CancellationToken ct)
    {
        var installer = http.RequestServices.GetService<ModulePackageInstaller>();
        var registry = http.RequestServices.GetService<IModuleRegistry>();
        if (installer is null || registry is null)
        {
            return ApiResults.ServiceUnavailableProblem("Module management is not configured.");
        }

        if (!registry.HasModule(moduleId))
        {
            return ApiResults.NotFoundProblem($"Module '{moduleId}' was not found.");
        }

        // 🔗 Refuse if other modules depend on this one (D11)~
        var dependents = new ModuleDependencyResolver().GetDependents(moduleId, registry.GetAllModules());
        if (dependents.Count > 0)
        {
            return ApiResults.ConflictProblem($"Module '{moduleId}' cannot be uninstalled — it is required by: {string.Join(", ", dependents)}.");
        }

        // 🛟 Refuse if executions are in flight (D11)~
        var tracker = http.RequestServices.GetService<IActiveExecutionTracker>();
        if (tracker is { HasActiveExecutions: true })
        {
            return ApiResults.ConflictProblem($"Module '{moduleId}' cannot be uninstalled while executions are in progress.");
        }

        Version? pinned = null;
        if (!string.IsNullOrWhiteSpace(version))
        {
            if (!Version.TryParse(version, out var parsed))
            {
                return ApiResults.BadRequestProblem($"Invalid version '{version}'.");
            }

            pinned = parsed;
        }

        var removed = await installer.UninstallAsync(moduleId, pinned, ct).ConfigureAwait(false);
        if (!removed)
        {
            // The module exists in the registry but wasn't installed from a package (builtin / host-wired)~
            return ApiResults.ConflictProblem($"Module '{moduleId}' is not a packaged module and cannot be uninstalled.");
        }

        return Results.NoContent();
    }

    private static IResult MapInstallFailure(ModuleInstallResult result)
    {
        var detail = result.Errors.Count > 0 ? string.Join("; ", result.Errors) : "Module installation failed.";
        return result.Reason switch
        {
            ModuleInstallFailureReason.DuplicateVersion => ApiResults.ConflictProblem(detail),
            _ => ApiResults.Problem422(detail),
        };
    }
}
