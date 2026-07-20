// <copyright file="ScriptsClient.cs" company="GlutenFree">
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
/// 🧪 Phase 3.4.0 — Typed client for the scripts REST surface (<c>/api/v1/scripts</c>):
/// sandbox test runs, language discovery, and library CRUD. ProblemDetails failures surface as
/// <see cref="ApiException"/> via the shared <see cref="ApiHttp"/> helpers~ ✨.
/// </summary>
public sealed class ScriptsClient
{
    private readonly HttpClient http;

    /// <summary>Initializes a new instance of the <see cref="ScriptsClient"/> class~ 🧪.</summary>
    /// <param name="http">The (auth-stamped) HTTP client.</param>
    public ScriptsClient(HttpClient http) => this.http = http;

    /// <summary>Runs a script in the sandbox (<c>POST /scripts/test</c>)~ ▶️.</summary>
    /// <param name="request">The test request (language/code/inputs/config/libraries).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The test result (success=false is a normal 200 body).</returns>
    public Task<ScriptTestResultDto> TestAsync(ScriptTestRequestDto request, CancellationToken ct = default)
        => ApiHttp.SendAsync<ScriptTestResultDto>(
            this.http,
            new HttpRequestMessage(HttpMethod.Post, "api/v1/scripts/test") { Content = JsonContent.Create(request, options: ApiHttp.Json) },
            ct);

    /// <summary>Lists the registered runnable languages (<c>GET /scripts/languages</c>)~ 🌐.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The registered languages.</returns>
    public Task<List<ScriptLanguageDto>> GetLanguagesAsync(CancellationToken ct = default)
        => ApiHttp.SendAsync<List<ScriptLanguageDto>>(this.http, new HttpRequestMessage(HttpMethod.Get, "api/v1/scripts/languages"), ct);

    /// <summary>Lists script libraries, optionally filtered by language (<c>GET /scripts/libraries</c>)~ 📚.</summary>
    /// <param name="language">Optional language filter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The libraries.</returns>
    public Task<List<ScriptLibraryDto>> ListLibrariesAsync(string? language = null, CancellationToken ct = default)
    {
        var url = "api/v1/scripts/libraries";
        if (!string.IsNullOrWhiteSpace(language))
        {
            url += $"?language={Uri.EscapeDataString(language)}";
        }

        return ApiHttp.SendAsync<List<ScriptLibraryDto>>(this.http, new HttpRequestMessage(HttpMethod.Get, url), ct);
    }

    /// <summary>Gets a single library, or null when absent (<c>GET /scripts/libraries/{id}</c>)~ 📚.</summary>
    /// <param name="libraryId">The library id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The library, or null.</returns>
    public async Task<ScriptLibraryDto?> GetLibraryAsync(string libraryId, CancellationToken ct = default)
    {
        try
        {
            return await ApiHttp.SendAsync<ScriptLibraryDto>(
                this.http, new HttpRequestMessage(HttpMethod.Get, $"api/v1/scripts/libraries/{Uri.EscapeDataString(libraryId)}"), ct).ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.Error.StatusCode == 404)
        {
            return null;
        }
    }

    /// <summary>Creates or updates a library (<c>PUT /scripts/libraries/{id}</c>)~ 💾.</summary>
    /// <param name="library">The library to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The stored library.</returns>
    public Task<ScriptLibraryDto> SaveLibraryAsync(ScriptLibraryDto library, CancellationToken ct = default)
        => ApiHttp.SendAsync<ScriptLibraryDto>(
            this.http,
            new HttpRequestMessage(HttpMethod.Put, $"api/v1/scripts/libraries/{Uri.EscapeDataString(library.LibraryId)}")
            {
                Content = JsonContent.Create(library, options: ApiHttp.Json),
            },
            ct);

    /// <summary>Deletes a library (<c>DELETE /scripts/libraries/{id}</c>)~ 🗑️.</summary>
    /// <param name="libraryId">The library id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    public Task DeleteLibraryAsync(string libraryId, CancellationToken ct = default)
        => ApiHttp.SendNoContentAsync(this.http, new HttpRequestMessage(HttpMethod.Delete, $"api/v1/scripts/libraries/{Uri.EscapeDataString(libraryId)}"), ct);
}
