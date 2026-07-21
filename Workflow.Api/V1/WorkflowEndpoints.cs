// <copyright file="WorkflowEndpoints.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.V1;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Api.Auth;
using Workflow.Api.Contracts;
using Workflow.Core.Models;
using Workflow.Modules.Validation;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;

/// <summary>
/// 📋 Phase 2.7.1 — Workflow definition CRUD endpoints over <see cref="IWorkflowRepository"/>~ ✨💖.
/// </summary>
public static class WorkflowEndpoints
{
    /// <summary>Registers the <c>/api/v1/workflows</c> endpoints~ 📋.</summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapV1Group().MapGroup("/workflows").WithTags("Workflows");

        group.MapGet("/", ListHandler).WithName("ListWorkflows").RequireAuthorization(AuthConstants.WorkflowReadPolicy);
        group.MapGet("/{id:guid}", GetHandler).WithName("GetWorkflow").RequireAuthorization(AuthConstants.WorkflowReadPolicy);
        group.MapPost("/", CreateHandler).WithName("CreateWorkflow").RequireAuthorization(AuthConstants.WorkflowWritePolicy);
        group.MapPost("/validate", ValidateHandler).WithName("ValidateWorkflow").RequireAuthorization(AuthConstants.WorkflowReadPolicy);
        group.MapPut("/{id:guid}", UpdateHandler).WithName("UpdateWorkflow").RequireAuthorization(AuthConstants.WorkflowWritePolicy);
        group.MapDelete("/{id:guid}", DeleteHandler).WithName("DeleteWorkflow").RequireAuthorization(AuthConstants.AdminPolicy);
        group.MapPost("/{id:guid}/restore", RestoreHandler).WithName("RestoreWorkflow").RequireAuthorization(AuthConstants.WorkflowWritePolicy);

        return app;
    }

    private static async Task<IResult> ListHandler(
        HttpContext http,
        string? name,
        string? tag,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var repo = http.RequestServices.GetService<IWorkflowRepository>();
        if (repo is null)
        {
            return ApiResults.ServiceUnavailableProblem("No persistence provider is configured.");
        }

        var filter = new WorkflowFilter(
            NameContains: string.IsNullOrWhiteSpace(name) ? null : name,
            IsActive: true,
            Tags: string.IsNullOrWhiteSpace(tag) ? null : new[] { tag });

        var paged = await repo.GetAllAsync(filter, PaginationBinding.From(page, pageSize), ct).ConfigureAwait(false);
        var dto = new PageDto<WorkflowSummaryDto>(
            paged.Items.Select(WorkflowSummaryDto.From).ToList(),
            paged.TotalCount,
            paged.Page,
            paged.PageSize,
            paged.TotalPages);

        return Results.Ok(dto);
    }

    private static async Task<IResult> GetHandler(HttpContext http, Guid id, CancellationToken ct)
    {
        var repo = http.RequestServices.GetService<IWorkflowRepository>();
        if (repo is null)
        {
            return ApiResults.ServiceUnavailableProblem("No persistence provider is configured.");
        }

        var definition = await repo.GetByIdAsync(id, ct).ConfigureAwait(false);
        return definition is null
            ? ApiResults.NotFoundProblem($"Workflow '{id}' was not found.")
            : Results.Ok(definition);
    }

    private static IResult ValidateHandler(
        WorkflowDefinition definition,
        ModuleAwareWorkflowValidator validator)
    {
        // 🧮 Phase 3.3 D14 — dry-run validation for the designer's save gate. No persistence.
        var result = validator.Validate(definition);
        var issues = new List<WorkflowValidationIssueDto>();
        foreach (var e in result.Errors)
        {
            issues.Add(new WorkflowValidationIssueDto("error", e.Message, e.NodeId));
        }

        foreach (var w in result.Warnings)
        {
            issues.Add(new WorkflowValidationIssueDto("warning", w.Message, w.NodeId));
        }

        return Results.Ok(new WorkflowValidationResultDto(result.IsValid, issues));
    }

    private static async Task<IResult> CreateHandler(
        HttpContext http,
        WorkflowDefinition definition,
        ModuleAwareWorkflowValidator validator,
        CancellationToken ct)
    {
        var repo = http.RequestServices.GetService<IWorkflowRepository>();
        if (repo is null)
        {
            return ApiResults.ServiceUnavailableProblem("No persistence provider is configured.");
        }

        // Server assigns the id on create — ignore any client-supplied id~
        var toCreate = definition with { Id = Guid.Empty };

        var validation = validator.Validate(toCreate);
        if (!validation.IsValid)
        {
            return ApiResults.ValidationProblem422(validation);
        }

        var id = await repo.CreateAsync(toCreate, ct).ConfigureAwait(false);
        var stored = await repo.GetByIdAsync(id, ct).ConfigureAwait(false);
        return Results.Created($"/api/v1/workflows/{id}", stored);
    }

    private static async Task<IResult> UpdateHandler(
        HttpContext http,
        Guid id,
        WorkflowDefinition definition,
        ModuleAwareWorkflowValidator validator,
        CancellationToken ct)
    {
        var repo = http.RequestServices.GetService<IWorkflowRepository>();
        if (repo is null)
        {
            return ApiResults.ServiceUnavailableProblem("No persistence provider is configured.");
        }

        var existing = await repo.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return ApiResults.NotFoundProblem($"Workflow '{id}' was not found.");
        }

        // Optimistic version guard: if the client sent an older/mismatched version, 409~
        if (definition.Version < existing.Version)
        {
            return ApiResults.ConflictProblem(
                $"Version conflict: incoming v{definition.Version} is older than stored v{existing.Version}.");
        }

        var toUpdate = definition with { Id = id };
        var validation = validator.Validate(toUpdate);
        if (!validation.IsValid)
        {
            return ApiResults.ValidationProblem422(validation);
        }

        await repo.UpdateAsync(id, toUpdate, ct).ConfigureAwait(false);
        var updated = await repo.GetByIdAsync(id, ct).ConfigureAwait(false);
        return Results.Ok(updated);
    }

    private static async Task<IResult> DeleteHandler(HttpContext http, Guid id, bool? purge, CancellationToken ct)
    {
        var repo = http.RequestServices.GetService<IWorkflowRepository>();
        if (repo is null)
        {
            return ApiResults.ServiceUnavailableProblem("No persistence provider is configured.");
        }

        if (purge == true)
        {
            // Refuse to purge if there are active (non-terminal) executions~ 🛡️
            var history = http.RequestServices.GetService<IExecutionHistoryRepository>();
            if (history is not null && await HasActiveExecutionsAsync(history, id, ct).ConfigureAwait(false))
            {
                return ApiResults.ConflictProblem($"Workflow '{id}' has active executions and cannot be purged.");
            }

            var purged = await repo.PurgeAsync(id, ct).ConfigureAwait(false);
            return purged ? Results.NoContent() : ApiResults.NotFoundProblem($"Workflow '{id}' was not found.");
        }

        var deleted = await repo.DeleteAsync(id, ct).ConfigureAwait(false);
        return deleted ? Results.NoContent() : ApiResults.NotFoundProblem($"Workflow '{id}' was not found.");
    }

    private static async Task<IResult> RestoreHandler(HttpContext http, Guid id, CancellationToken ct)
    {
        var repo = http.RequestServices.GetService<IWorkflowRepository>();
        if (repo is null)
        {
            return ApiResults.ServiceUnavailableProblem("No persistence provider is configured.");
        }

        var restored = await repo.RestoreAsync(id, ct).ConfigureAwait(false);
        return restored ? Results.NoContent() : ApiResults.NotFoundProblem($"Workflow '{id}' was not found or not deleted.");
    }

    private static async Task<bool> HasActiveExecutionsAsync(IExecutionHistoryRepository history, Guid workflowId, CancellationToken ct)
    {
        var filter = new ExecutionFilter(States: new[] { ExecutionState.Pending, ExecutionState.Running, ExecutionState.Paused });
        var page = await history.GetExecutionsForWorkflowAsync(workflowId, filter, new Pagination(1, 1), ct).ConfigureAwait(false);
        return page.TotalCount > 0;
    }
}
