// <copyright file="VariablesClient.cs" company="GlutenFree">
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
/// 🔧 Phase 3.5 (V6) — typed client for the scoped variable store
/// (<c>/api/v1/variables</c>): global values shared by every workflow, per-workflow values a run
/// can persist, and per-execution values~ ✨.
/// </summary>
/// <remarks>
/// Note the list endpoint returns a plain <c>name → value</c> map, not entry objects — only the
/// single-variable and history endpoints project <see cref="VariableDto"/>.
/// </remarks>
public sealed class VariablesClient
{
    /// <summary>The global scope — values shared across every workflow~ 🌍.</summary>
    public const string GlobalScope = "global";

    /// <summary>The per-workflow scope — values a run can persist for the next one~ 📋.</summary>
    public const string WorkflowScope = "workflow";

    /// <summary>The per-execution scope — values that vanish with the run~ ⚡.</summary>
    public const string ExecutionScope = "execution";

    private readonly HttpClient http;

    /// <summary>Initializes a new instance of the <see cref="VariablesClient"/> class~ 🔧.</summary>
    /// <param name="http">The (auth-stamped) HTTP client.</param>
    public VariablesClient(HttpClient http) => this.http = http;

    /// <summary>Lists every variable in a scope as a name → value map~ 📋.</summary>
    /// <param name="scope">The scope (defaults to global).</param>
    /// <param name="scopeId">The workflow/execution id, required for non-global scopes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The variables, keyed by name.</returns>
    public Task<Dictionary<string, JsonElement>> ListAsync(
        string scope = GlobalScope,
        Guid? scopeId = null,
        CancellationToken ct = default)
        => ApiHttp.SendAsync<Dictionary<string, JsonElement>>(
            this.http,
            new HttpRequestMessage(HttpMethod.Get, Url(null, scope, scopeId)),
            ct);

    /// <summary>Gets one variable, or null when it doesn't exist~ 🔍.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="scope">The scope.</param>
    /// <param name="scopeId">The workflow/execution id, for non-global scopes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The entry, or null.</returns>
    public async Task<VariableDto?> GetAsync(
        string name,
        string scope = GlobalScope,
        Guid? scopeId = null,
        CancellationToken ct = default)
    {
        try
        {
            return await ApiHttp.SendAsync<VariableDto>(
                this.http, new HttpRequestMessage(HttpMethod.Get, Url(name, scope, scopeId)), ct).ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.Error.StatusCode == 404)
        {
            return null;
        }
    }

    /// <summary>Gets a variable's full version history, oldest first~ 📜.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="scope">The scope.</param>
    /// <param name="scopeId">The workflow/execution id, for non-global scopes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Every stored version.</returns>
    public Task<List<VariableDto>> HistoryAsync(
        string name,
        string scope = GlobalScope,
        Guid? scopeId = null,
        CancellationToken ct = default)
        => ApiHttp.SendAsync<List<VariableDto>>(
            this.http,
            new HttpRequestMessage(HttpMethod.Get, Url($"{name}/history", scope, scopeId)),
            ct);

    /// <summary>Sets a variable, creating a new version~ 💾.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value; a JSON null stores a present null.</param>
    /// <param name="scope">The scope.</param>
    /// <param name="scopeId">The workflow/execution id, for non-global scopes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The stored entry.</returns>
    public Task<VariableDto> SetAsync(
        string name,
        JsonElement? value,
        string scope = GlobalScope,
        Guid? scopeId = null,
        CancellationToken ct = default)
        => ApiHttp.SendAsync<VariableDto>(
            this.http,
            new HttpRequestMessage(HttpMethod.Put, Url(name, scope, scopeId))
            {
                Content = JsonContent.Create(new SetVariableRequest(value), options: ApiHttp.Json),
            },
            ct);

    /// <summary>Hard-deletes a variable and all its history~ 🗑️.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="scope">The scope.</param>
    /// <param name="scopeId">The workflow/execution id, for non-global scopes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    /// <summary>🔒 Plan 08 — changes a variable's secret mode. A 409 means earlier values would be erased: retry with <paramref name="confirmPurge"/>.</summary>
    /// <param name="name">The variable.</param>
    /// <param name="mode"><c>None</c>, <c>Secret</c> or <c>FollowWorkflows</c>.</param>
    /// <param name="value">A value to store with the change (required when a secret becomes None).</param>
    /// <param name="confirmPurge">Whether the user confirmed erasing earlier values.</param>
    /// <param name="scope">The scope.</param>
    /// <param name="scopeId">The workflow, for workflow scope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The variable as stored.</returns>
    public Task<VariableDto> SetSecretModeAsync(
        string name,
        string mode,
        JsonElement? value,
        bool confirmPurge,
        string scope = GlobalScope,
        Guid? scopeId = null,
        CancellationToken ct = default)
        => ApiHttp.SendAsync<VariableDto>(
            this.http,
            new HttpRequestMessage(HttpMethod.Put, Url($"{name}/secret", scope, scopeId) + (confirmPurge ? "&confirmPurge=true" : string.Empty))
            {
                Content = JsonContent.Create(new { mode, value }, options: ApiHttp.Json),
            },
            ct);

    /// <summary>🔒 Plan 08 — which stored variables saving a workflow (declaring these secrets) would turn secret.</summary>
    /// <param name="workflowId">The workflow.</param>
    /// <param name="secretNames">The variables it declares secret.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The affected stored variables.</returns>
    public Task<List<SecretImpactDto>> SecretImpactAsync(Guid workflowId, IEnumerable<string> secretNames, CancellationToken ct = default)
        => ApiHttp.SendAsync<List<SecretImpactDto>>(
            this.http,
            new HttpRequestMessage(HttpMethod.Post, "api/v1/variables/secret-impact")
            {
                Content = JsonContent.Create(new { workflowId, secretNames = secretNames.ToList() }, options: ApiHttp.Json),
            },
            ct);

    public Task DeleteAsync(
        string name,
        string scope = GlobalScope,
        Guid? scopeId = null,
        CancellationToken ct = default)
        => ApiHttp.SendNoContentAsync(
            this.http,
            new HttpRequestMessage(HttpMethod.Delete, Url(name, scope, scopeId)),
            ct);

    private static string Url(string? segment, string scope, Guid? scopeId)
    {
        var url = segment is null
            ? $"api/v1/variables?scope={Uri.EscapeDataString(scope)}"
            : $"api/v1/variables/{segment}?scope={Uri.EscapeDataString(scope)}";

        return scopeId is { } id ? $"{url}&scopeId={id}" : url;
    }
}
