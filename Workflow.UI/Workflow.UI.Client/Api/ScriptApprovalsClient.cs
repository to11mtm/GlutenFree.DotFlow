// <copyright file="ScriptApprovalsClient.cs" company="GlutenFree">
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
/// 🛡️ §9.2 — Typed client for <c>/api/v1/script-approvals</c>: the review queue for C# code that
/// uses sensitive APIs (list, get, request, approve, reject)~ ✨.
/// </summary>
public sealed class ScriptApprovalsClient
{
    private readonly HttpClient http;

    /// <summary>Initializes a new instance of the <see cref="ScriptApprovalsClient"/> class.</summary>
    /// <param name="http">The (auth-stamped) HTTP client.</param>
    public ScriptApprovalsClient(HttpClient http) => this.http = http;

    /// <summary>Lists approval records, optionally filtered by status.</summary>
    /// <param name="status"><c>Candidate</c>, <c>Requested</c>, <c>Approved</c>, <c>Rejected</c>, or null for all.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The records, newest first.</returns>
    public Task<List<ScriptApprovalDto>> ListAsync(string? status = null, CancellationToken ct = default)
    {
        var url = "api/v1/script-approvals";
        if (!string.IsNullOrWhiteSpace(status))
        {
            url += $"?status={Uri.EscapeDataString(status)}";
        }

        return ApiHttp.SendAsync<List<ScriptApprovalDto>>(this.http, new HttpRequestMessage(HttpMethod.Get, url), ct);
    }

    /// <summary>Gets one record, or null when the hash is unknown.</summary>
    /// <param name="hash">The code hash.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The record, or null.</returns>
    public async Task<ScriptApprovalDto?> GetAsync(string hash, CancellationToken ct = default)
    {
        try
        {
            return await ApiHttp.SendAsync<ScriptApprovalDto>(
                this.http, new HttpRequestMessage(HttpMethod.Get, $"api/v1/script-approvals/{Uri.EscapeDataString(hash)}"), ct).ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.Error.StatusCode == 404)
        {
            return null;
        }
    }

    /// <summary>Asks for review of flagged code (the author).</summary>
    /// <param name="hash">The code hash.</param>
    /// <param name="note">Why the code needs these APIs.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated record.</returns>
    public Task<ScriptApprovalDto> RequestAsync(string hash, string? note, CancellationToken ct = default)
        => this.PostAsync(hash, "request", note, ct);

    /// <summary>Approves requested code (a ScriptApprover/Admin who isn't the author).</summary>
    /// <param name="hash">The code hash.</param>
    /// <param name="note">The reviewer's note.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated record.</returns>
    public Task<ScriptApprovalDto> ApproveAsync(string hash, string? note, CancellationToken ct = default)
        => this.PostAsync(hash, "approve", note, ct);

    /// <summary>Rejects requested code.</summary>
    /// <param name="hash">The code hash.</param>
    /// <param name="note">The reviewer's note (why).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated record.</returns>
    public Task<ScriptApprovalDto> RejectAsync(string hash, string? note, CancellationToken ct = default)
        => this.PostAsync(hash, "reject", note, ct);

    private Task<ScriptApprovalDto> PostAsync(string hash, string action, string? note, CancellationToken ct)
        => ApiHttp.SendAsync<ScriptApprovalDto>(
            this.http,
            new HttpRequestMessage(HttpMethod.Post, $"api/v1/script-approvals/{Uri.EscapeDataString(hash)}/{action}")
            {
                Content = JsonContent.Create(new ScriptApprovalNoteDto(note), options: ApiHttp.Json),
            },
            ct);
}
