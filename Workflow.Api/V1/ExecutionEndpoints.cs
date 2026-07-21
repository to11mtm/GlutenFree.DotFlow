// <copyright file="ExecutionEndpoints.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.V1;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Api.Auth;
using Workflow.Api.Contracts;
using Workflow.Api.Execution;
using Workflow.Core.Models;
using Workflow.Engine.Messages;
using Workflow.Modules.Internal;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;

/// <summary>
/// ⚡ Phase 2.7.2 — Execution endpoints (start / by-name / sync / status / cancel / list)~ ✨💖.
/// </summary>
public static class ExecutionEndpoints
{
    /// <summary>Registers the execution endpoints under <c>/api/v1</c>~ ⚡.</summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapExecutionEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapV1Group();

        var workflows = v1.MapGroup("/workflows").WithTags("Executions");
        workflows.MapPost("/{id:guid}/execute", ExecuteHandler).WithName("ExecuteWorkflow").RequireAuthorization(AuthConstants.WorkflowExecutePolicy);
        workflows.MapPost("/{id:guid}/execute/sync", ExecuteSyncHandler).WithName("ExecuteWorkflowSync").RequireAuthorization(AuthConstants.WorkflowExecutePolicy);
        workflows.MapPost("/execute/{name}", ExecuteByNameHandler).WithName("ExecuteWorkflowByName").RequireAuthorization(AuthConstants.WorkflowExecutePolicy);

        var executions = v1.MapGroup("/executions").WithTags("Executions");
        executions.MapGet("/{executionId:guid}", StatusHandler).WithName("GetExecution").RequireAuthorization(AuthConstants.WorkflowReadPolicy);
        executions.MapPost("/{executionId:guid}/cancel", CancelHandler).WithName("CancelExecution").RequireAuthorization(AuthConstants.WorkflowExecutePolicy);
        executions.MapGet("/", ListHandler).WithName("ListExecutions").RequireAuthorization(AuthConstants.WorkflowReadPolicy);

        return app;
    }

    private static async Task<IResult> ExecuteHandler(
        HttpContext http,
        Guid id,
        StartExecutionRequest? request,
        CancellationToken ct)
    {
        var service = http.RequestServices.GetService<IWorkflowExecutionService>();
        if (service is null)
        {
            return ApiResults.ServiceUnavailableProblem("No persistence provider is configured.");
        }

        var (inputs, options) = BuildStart(http, request);
        var result = await service.StartAsync(id, inputs, options, ct).ConfigureAwait(false);
        if (!result.Success)
        {
            return MapStartFailure(id, result);
        }

        http.Response.Headers["Location"] = $"/api/v1/executions/{result.ExecutionId}";
        return Results.Accepted(
            $"/api/v1/executions/{result.ExecutionId}",
            new ExecutionStartedDto(result.ExecutionId, "accepted"));
    }

    private static async Task<IResult> ExecuteSyncHandler(
        HttpContext http,
        Guid id,
        StartExecutionRequest? request,
        int? timeoutSeconds,
        CancellationToken ct)
    {
        var service = http.RequestServices.GetService<IWorkflowExecutionService>();
        if (service is null)
        {
            return ApiResults.ServiceUnavailableProblem("No persistence provider is configured.");
        }

        var (inputs, options) = BuildStart(http, request);
        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds ?? 30, 1, 300));

        var (start, final, timedOut) = await service.StartAndWaitAsync(id, inputs, options, timeout, ct).ConfigureAwait(false);
        if (!start.Success)
        {
            return MapStartFailure(id, start);
        }

        if (timedOut)
        {
            // 202 + continuation poll URL (Q5)~
            http.Response.Headers["Location"] = $"/api/v1/executions/{start.ExecutionId}";
            return Results.Accepted(
                $"/api/v1/executions/{start.ExecutionId}",
                new { executionId = start.ExecutionId, status = "running" });
        }

        return Results.Ok(ExecutionStatusDto.From(final!));
    }

    private static async Task<IResult> ExecuteByNameHandler(
        HttpContext http,
        string name,
        string? version,
        StartExecutionRequest? request,
        CancellationToken ct)
    {
        var repo = http.RequestServices.GetService<IWorkflowRepository>();
        var service = http.RequestServices.GetService<IWorkflowExecutionService>();
        if (repo is null || service is null)
        {
            return ApiResults.ServiceUnavailableProblem("No persistence provider is configured.");
        }

        var definition = await ResolveByNameAsync(repo, name, version, ct).ConfigureAwait(false);
        if (definition is null)
        {
            return ApiResults.NotFoundProblem($"No active workflow named '{name}'{(version is null ? string.Empty : $" v{version}")} was found.");
        }

        var (inputs, options) = BuildStart(http, request);
        var result = await service.StartAsync(definition.Id, inputs, options, ct).ConfigureAwait(false);
        if (!result.Success)
        {
            return MapStartFailure(definition.Id, result);
        }

        http.Response.Headers["Location"] = $"/api/v1/executions/{result.ExecutionId}";
        return Results.Accepted(
            $"/api/v1/executions/{result.ExecutionId}",
            new ExecutionStartedDto(result.ExecutionId, "accepted"));
    }

    private static async Task<IResult> StatusHandler(HttpContext http, Guid executionId, CancellationToken ct)
    {
        var service = http.RequestServices.GetService<IWorkflowExecutionService>();
        if (service is null)
        {
            return ApiResults.ServiceUnavailableProblem("No persistence provider is configured.");
        }

        var status = await service.GetStatusAsync(executionId, ct).ConfigureAwait(false);
        return status is null
            ? ApiResults.NotFoundProblem($"Execution '{executionId}' was not found.")
            : Results.Ok(ExecutionStatusDto.From(status));
    }

    private static async Task<IResult> CancelHandler(HttpContext http, Guid executionId, CancellationToken ct)
    {
        var service = http.RequestServices.GetService<IWorkflowExecutionService>();
        if (service is null)
        {
            return ApiResults.ServiceUnavailableProblem("No persistence provider is configured.");
        }

        var cancelled = await service.CancelAsync(executionId, ct).ConfigureAwait(false);
        return cancelled
            ? Results.Ok(new { executionId, cancelled = true })
            : ApiResults.NotFoundProblem($"Execution '{executionId}' was not found.");
    }

    private static async Task<IResult> ListHandler(
        HttpContext http,
        Guid workflowId,
        string? status,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var history = http.RequestServices.GetService<IExecutionHistoryRepository>();
        if (history is null)
        {
            return ApiResults.ServiceUnavailableProblem("No persistence provider is configured.");
        }

        if (workflowId == Guid.Empty)
        {
            return ApiResults.BadRequestProblem("A workflowId query parameter is required.");
        }

        ExecutionState[]? states = null;
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ExecutionState>(status, ignoreCase: true, out var parsed))
        {
            states = new[] { parsed };
        }

        var filter = new ExecutionFilter(States: states, StartedAfter: from, StartedBefore: to);
        var paged = await history.GetExecutionsForWorkflowAsync(workflowId, filter, PaginationBinding.From(page, pageSize), ct).ConfigureAwait(false);

        var dto = new PageDto<ExecutionDto>(
            paged.Items.Select(r => new ExecutionDto(r.ExecutionId, r.WorkflowId, r.State.ToString(), r.StartedAt, r.CompletedAt, r.TriggeredBy)).ToList(),
            paged.TotalCount,
            paged.Page,
            paged.PageSize,
            paged.TotalPages);

        return Results.Ok(dto);
    }

    private static (IReadOnlyDictionary<string, object?> Inputs, ExecutionStartOptions Options) BuildStart(
        HttpContext http,
        StartExecutionRequest? request)
    {
        var inputs = new Dictionary<string, object?>();
        if (request?.Inputs is not null)
        {
            foreach (var (key, value) in request.Inputs)
            {
                inputs[key] = value is System.Text.Json.JsonElement je ? JsonValueConverter.FromElement(je) : value;
            }
        }

        var writeMode = ParseWriteMode(request?.VariableWriteMode);
        var options = new ExecutionStartOptions(http.ResolveCallerId(), writeMode);
        return (inputs, options);
    }

    private static VariableWriteMode ParseWriteMode(string? value)
        => value?.ToLowerInvariant() switch
        {
            "workflow" => VariableWriteMode.Workflow,
            "dual" => VariableWriteMode.Dual,
            _ => VariableWriteMode.Execution,
        };

    private static async Task<WorkflowDefinition?> ResolveByNameAsync(
        IWorkflowRepository repo,
        string name,
        string? version,
        CancellationToken ct)
    {
        // Filter by name, then pick the exact-name match with the newest version (or the pinned version)~
        var page = await repo.GetAllAsync(
            new WorkflowFilter(NameContains: name, IsActive: true),
            new Pagination(1, Pagination.MaxPageSize),
            ct).ConfigureAwait(false);

        var matches = page.Items.Where(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(version) && Version.TryParse(version, out var pinned))
        {
            return matches.FirstOrDefault(d => d.Version == pinned);
        }

        return matches.OrderByDescending(d => d.Version).FirstOrDefault();
    }

    private static IResult MapStartFailure(Guid workflowId, StartResult result)
    {
        var detail = result.Errors.Count > 0 ? string.Join("; ", result.Errors) : "Failed to start execution.";
        return detail.Contains("not found", StringComparison.OrdinalIgnoreCase)
            ? ApiResults.NotFoundProblem($"Workflow '{workflowId}' was not found.")
            : ApiResults.BadRequestProblem(detail);
    }
}
