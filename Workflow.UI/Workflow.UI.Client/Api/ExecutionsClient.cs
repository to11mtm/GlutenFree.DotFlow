// <copyright file="ExecutionsClient.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Api;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Workflow.UI.Client.Api.Dtos;

/// <summary>
/// ⚡ Phase 3.3.a.0 — Typed client for execution start/status/cancel/list
/// (<c>/api/v1/workflows/{id}/execute</c>, <c>/api/v1/executions</c>)~ ✨.
/// </summary>
public sealed class ExecutionsClient
{
    private readonly HttpClient http;

    /// <summary>Initializes a new instance of the <see cref="ExecutionsClient"/> class~ ⚡.</summary>
    /// <param name="http">The (auth-stamped) HTTP client.</param>
    public ExecutionsClient(HttpClient http) => this.http = http;

    /// <summary>Starts an execution of a workflow~ ▶️.</summary>
    /// <param name="workflowId">The workflow id.</param>
    /// <param name="inputs">Optional initial inputs.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The started-execution result.</returns>
    public Task<ExecutionStartedDto> ExecuteAsync(Guid workflowId, Dictionary<string, JsonElement>? inputs = null, CancellationToken ct = default)
    {
        var body = new StartExecutionRequest(inputs, null);
        var request = new HttpRequestMessage(HttpMethod.Post, $"api/v1/workflows/{workflowId}/execute")
        {
            Content = JsonContent.Create(body, options: ApiHttp.Json),
        };
        return ApiHttp.SendAsync<ExecutionStartedDto>(this.http, request, ct);
    }

    /// <summary>Gets the current status of an execution~ 📊.</summary>
    /// <param name="executionId">The execution id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The status, or null if unknown.</returns>
    public async Task<ExecutionStatusDto?> GetStatusAsync(Guid executionId, CancellationToken ct = default)
    {
        try
        {
            return await ApiHttp.SendAsync<ExecutionStatusDto>(
                this.http, new HttpRequestMessage(HttpMethod.Get, $"api/v1/executions/{executionId}"), ct).ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.Error.StatusCode == 404)
        {
            return null;
        }
    }

    /// <summary>Requests cancellation of a running execution~ 🛑.</summary>
    /// <param name="executionId">The execution id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    public Task CancelAsync(Guid executionId, CancellationToken ct = default)
        => ApiHttp.SendNoContentAsync(this.http, new HttpRequestMessage(HttpMethod.Post, $"api/v1/executions/{executionId}/cancel"), ct);

    /// <summary>Lists executions for a workflow (paged)~ 🕘.</summary>
    /// <param name="workflowId">The workflow id.</param>
    /// <param name="page">1-based page.</param>
    /// <param name="pageSize">Page size.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page of execution rows.</returns>
    public Task<PageDto<ExecutionDto>> ListAsync(Guid workflowId, int page = 1, int pageSize = 20, CancellationToken ct = default)
        => ApiHttp.SendAsync<PageDto<ExecutionDto>>(
            this.http,
            new HttpRequestMessage(HttpMethod.Get, $"api/v1/executions?workflowId={workflowId}&page={page}&pageSize={pageSize}"),
            ct);
}
