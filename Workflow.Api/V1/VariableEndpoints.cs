// <copyright file="VariableEndpoints.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.V1;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Api.Auth;
using Workflow.Api.Contracts;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;

/// <summary>
/// 🔧 Phase 2.7.4 — Scoped, versioned variable management over <see cref="IVariableStore"/>~ ✨💖.
/// </summary>
public static class VariableEndpoints
{
    /// <summary>Registers the <c>/api/v1/variables</c> endpoints~ 🔧.</summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapVariableEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapV1Group().MapGroup("/variables").WithTags("Variables");

        group.MapGet("/", ListHandler).WithName("ListVariables").RequireAuthorization(AuthConstants.WorkflowReadPolicy);
        group.MapGet("/{name}", GetHandler).WithName("GetVariable").RequireAuthorization(AuthConstants.WorkflowReadPolicy);
        group.MapGet("/{name}/history", HistoryHandler).WithName("GetVariableHistory").RequireAuthorization(AuthConstants.WorkflowReadPolicy);
        group.MapPut("/{name}", SetHandler).WithName("SetVariable").RequireAuthorization(AuthConstants.WorkflowWritePolicy);
        group.MapDelete("/{name}", DeleteHandler).WithName("DeleteVariable").RequireAuthorization(AuthConstants.WorkflowWritePolicy);

        return app;
    }

    private static async Task<IResult> ListHandler(
        HttpContext http,
        string? scope,
        Guid? scopeId,
        CancellationToken ct)
    {
        var store = http.RequestServices.GetService<IVariableStore>();
        if (store is null)
        {
            return ApiResults.ServiceUnavailableProblem("No persistence provider is configured.");
        }

        if (!TryBuildScope(scope, scopeId, out var variableScope, out var error))
        {
            return ApiResults.BadRequestProblem(error);
        }

        var all = await store.GetAllVariablesAsync(variableScope, ct).ConfigureAwait(false);
        return Results.Ok(all);
    }

    private static async Task<IResult> GetHandler(
        HttpContext http,
        string name,
        string? scope,
        Guid? scopeId,
        int? version,
        CancellationToken ct)
    {
        var store = http.RequestServices.GetService<IVariableStore>();
        if (store is null)
        {
            return ApiResults.ServiceUnavailableProblem("No persistence provider is configured.");
        }

        if (!TryBuildScope(scope, scopeId, out var variableScope, out var error))
        {
            return ApiResults.BadRequestProblem(error);
        }

        var entry = await store.GetVariableAsync(variableScope, name, version, ct).ConfigureAwait(false);
        return entry is null
            ? ApiResults.NotFoundProblem($"Variable '{name}' was not found in {variableScope.Kind} scope.")
            : Results.Ok(VariableDto.From(entry));
    }

    private static async Task<IResult> HistoryHandler(
        HttpContext http,
        string name,
        string? scope,
        Guid? scopeId,
        CancellationToken ct)
    {
        var store = http.RequestServices.GetService<IVariableStore>();
        if (store is null)
        {
            return ApiResults.ServiceUnavailableProblem("No persistence provider is configured.");
        }

        if (!TryBuildScope(scope, scopeId, out var variableScope, out var error))
        {
            return ApiResults.BadRequestProblem(error);
        }

        var history = await store.GetVariableHistoryAsync(variableScope, name, ct).ConfigureAwait(false);
        var dtos = new List<VariableDto>(history.Count);
        foreach (var entry in history)
        {
            dtos.Add(VariableDto.From(entry));
        }

        return Results.Ok(dtos);
    }

    private static async Task<IResult> SetHandler(
        HttpContext http,
        string name,
        string? scope,
        Guid? scopeId,
        SetVariableRequest? request,
        CancellationToken ct)
    {
        var store = http.RequestServices.GetService<IVariableStore>();
        if (store is null)
        {
            return ApiResults.ServiceUnavailableProblem("No persistence provider is configured.");
        }

        if (!TryBuildScope(scope, scopeId, out var variableScope, out var error))
        {
            return ApiResults.BadRequestProblem(error);
        }

        await store.SetVariableAsync(variableScope, name, request?.ToClrValue(), ct).ConfigureAwait(false);
        var entry = await store.GetVariableAsync(variableScope, name, null, ct).ConfigureAwait(false);
        return entry is null
            ? ApiResults.ServiceUnavailableProblem("The variable could not be read back after writing.")
            : Results.Ok(VariableDto.From(entry));
    }

    private static async Task<IResult> DeleteHandler(
        HttpContext http,
        string name,
        string? scope,
        Guid? scopeId,
        CancellationToken ct)
    {
        var store = http.RequestServices.GetService<IVariableStore>();
        if (store is null)
        {
            return ApiResults.ServiceUnavailableProblem("No persistence provider is configured.");
        }

        if (!TryBuildScope(scope, scopeId, out var variableScope, out var error))
        {
            return ApiResults.BadRequestProblem(error);
        }

        var deleted = await store.DeleteVariableAsync(variableScope, name, ct).ConfigureAwait(false);
        return deleted
            ? Results.NoContent()
            : ApiResults.NotFoundProblem($"Variable '{name}' was not found in {variableScope.Kind} scope.");
    }

    private static bool TryBuildScope(string? scope, Guid? scopeId, out VariableScope variableScope, out string error)
    {
        variableScope = VariableScope.Global;
        error = string.Empty;

        switch ((scope ?? "global").Trim().ToLowerInvariant())
        {
            case "global":
                variableScope = VariableScope.Global;
                return true;
            case "workflow":
                if (scopeId is null || scopeId == Guid.Empty)
                {
                    error = "A scopeId query parameter is required for workflow scope.";
                    return false;
                }

                variableScope = VariableScope.ForWorkflow(scopeId.Value);
                return true;
            case "execution":
                if (scopeId is null || scopeId == Guid.Empty)
                {
                    error = "A scopeId query parameter is required for execution scope.";
                    return false;
                }

                variableScope = VariableScope.ForExecution(scopeId.Value);
                return true;
            default:
                error = $"Unknown scope '{scope}'. Use global, workflow, or execution.";
                return false;
        }
    }
}
