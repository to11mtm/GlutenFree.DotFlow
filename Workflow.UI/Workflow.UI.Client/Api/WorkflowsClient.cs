// <copyright file="WorkflowsClient.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Api;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Workflow.UI.Client.Api.Dtos;

/// <summary>
/// 📋 Phase 3.3.a.0 — Typed client for the workflows REST surface
/// (<c>/api/v1/workflows</c>). ProblemDetails failures surface as <see cref="ApiException"/>~ ✨.
/// </summary>
public sealed class WorkflowsClient
{
    private readonly HttpClient http;

    /// <summary>Initializes a new instance of the <see cref="WorkflowsClient"/> class~ 📋.</summary>
    /// <param name="http">The (auth-stamped) HTTP client.</param>
    public WorkflowsClient(HttpClient http) => this.http = http;

    /// <summary>Lists workflows (paged, optional name filter)~ 📋.</summary>
    /// <param name="search">Optional name-contains filter.</param>
    /// <param name="page">1-based page.</param>
    /// <param name="pageSize">Page size.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page of summaries.</returns>
    public Task<PageDto<WorkflowSummaryDto>> ListAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var url = $"api/v1/workflows?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search))
        {
            url += $"&name={Uri.EscapeDataString(search)}";
        }

        return ApiHttp.SendAsync<PageDto<WorkflowSummaryDto>>(this.http, new HttpRequestMessage(HttpMethod.Get, url), ct);
    }

    /// <summary>Gets a full workflow definition~ 📋.</summary>
    /// <param name="id">The workflow id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The workflow.</returns>
    public Task<WorkflowDto> GetAsync(Guid id, CancellationToken ct = default)
        => ApiHttp.SendAsync<WorkflowDto>(this.http, new HttpRequestMessage(HttpMethod.Get, $"api/v1/workflows/{id}"), ct);

    /// <summary>Creates a new workflow (server assigns the id)~ ➕.</summary>
    /// <param name="workflow">The definition to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The stored workflow (with server id).</returns>
    public Task<WorkflowDto> CreateAsync(WorkflowDto workflow, CancellationToken ct = default)
        => ApiHttp.SendAsync<WorkflowDto>(this.http, JsonRequest(HttpMethod.Post, "api/v1/workflows", workflow), ct);

    /// <summary>Updates an existing workflow~ 💾.</summary>
    /// <param name="workflow">The definition to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The stored workflow.</returns>
    public Task<WorkflowDto> UpdateAsync(WorkflowDto workflow, CancellationToken ct = default)
        => ApiHttp.SendAsync<WorkflowDto>(this.http, JsonRequest(HttpMethod.Put, $"api/v1/workflows/{workflow.Id}", workflow), ct);

    /// <summary>Deletes a workflow~ 🗑️.</summary>
    /// <param name="id">The workflow id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    public Task DeleteAsync(Guid id, CancellationToken ct = default)
        => ApiHttp.SendNoContentAsync(this.http, new HttpRequestMessage(HttpMethod.Delete, $"api/v1/workflows/{id}"), ct);

    /// <summary>Validates a workflow server-side without persisting (D14)~ ✅.</summary>
    /// <param name="workflow">The definition to validate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The validation result.</returns>
    public Task<WorkflowValidationDto> ValidateAsync(WorkflowDto workflow, CancellationToken ct = default)
        => ApiHttp.SendAsync<WorkflowValidationDto>(this.http, JsonRequest(HttpMethod.Post, "api/v1/workflows/validate", workflow), ct);

    private static HttpRequestMessage JsonRequest<T>(HttpMethod method, string url, T body)
        => new(method, url) { Content = JsonContent.Create(body, options: ApiHttp.Json) };
}

/// <summary>✅ Phase 3.3.b.4 — Result of the server validate endpoint (D14)~ ✨.</summary>
/// <param name="Valid">Whether the workflow passed validation.</param>
/// <param name="Issues">The issues found (empty when valid).</param>
public sealed record WorkflowValidationDto(bool Valid, List<WorkflowValidationIssueDto> Issues);

/// <summary>✅ Phase 3.3.b.4 — A single validation issue~ ✨.</summary>
/// <param name="Severity">error/warning.</param>
/// <param name="Message">The message.</param>
/// <param name="NodeId">The offending node id, when applicable.</param>
public sealed record WorkflowValidationIssueDto(string Severity, string Message, string? NodeId);
