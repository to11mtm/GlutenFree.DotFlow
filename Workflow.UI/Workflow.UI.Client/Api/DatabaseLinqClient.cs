// <copyright file="DatabaseLinqClient.cs" company="GlutenFree">
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
/// 🧬 Linq Studio — typed client for the typed-linq authoring surface
/// (<c>/api/database/linq/*</c> + <c>/api/database/catalog/*</c>). ProblemDetails failures surface
/// as <see cref="ApiException"/> via the shared <see cref="ApiHttp"/> helpers~ ✨.
/// </summary>
public sealed class DatabaseLinqClient
{
    private readonly HttpClient http;

    /// <summary>Initializes a new instance of the <see cref="DatabaseLinqClient"/> class~ 🧬.</summary>
    /// <param name="http">The (auth-stamped) HTTP client.</param>
    public DatabaseLinqClient(HttpClient http) => this.http = http;

    /// <summary>Compiles without persisting; returns diagnostics (<c>POST /linq/validate</c>)~ ✅.</summary>
    /// <param name="request">The authoring request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The validate response.</returns>
    public Task<LinqValidateResponseDto> ValidateAsync(LinqAuthoringRequestDto request, CancellationToken ct = default)
        => ApiHttp.SendAsync<LinqValidateResponseDto>(
            this.http,
            new HttpRequestMessage(HttpMethod.Post, "api/database/linq/validate") { Content = JsonContent.Create(request, options: ApiHttp.Json) },
            ct);

    /// <summary>Compiles + runs against generated sample data (<c>POST /linq/preview</c>)~ 👀.</summary>
    /// <param name="request">The authoring request (with sample input values).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The preview response.</returns>
    public Task<LinqPreviewResponseDto> PreviewAsync(LinqAuthoringRequestDto request, CancellationToken ct = default)
        => ApiHttp.SendAsync<LinqPreviewResponseDto>(
            this.http,
            new HttpRequestMessage(HttpMethod.Post, "api/database/linq/preview") { Content = JsonContent.Create(request, options: ApiHttp.Json) },
            ct);

    /// <summary>Compiles + caches; returns the assembly key (<c>POST /linq/compile</c>)~ 🔑.</summary>
    /// <param name="request">The authoring request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The compile response.</returns>
    public Task<LinqCompileResponseDto> CompileAsync(LinqAuthoringRequestDto request, CancellationToken ct = default)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, "api/database/linq/compile")
        {
            Content = JsonContent.Create(request, options: ApiHttp.Json),
        };

        // 🔐 Placeholder trusted-author gate header (matches the API's demo default).
        msg.Headers.Add("X-Trusted-Author", "true");
        return ApiHttp.SendAsync<LinqCompileResponseDto>(this.http, msg, ct);
    }

    /// <summary>Lists the named database connections (<c>GET /api/database/connections</c>)~ 📇.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The connections.</returns>
    public Task<List<DbConnectionDto>> ListConnectionsAsync(CancellationToken ct = default)
        => ApiHttp.SendAsync<List<DbConnectionDto>>(
            this.http,
            new HttpRequestMessage(HttpMethod.Get, "api/database/connections/"),
            ct);

    /// <summary>Creates/updates a named connection (<c>POST /api/database/connections</c>)~ 💾.</summary>
    /// <param name="body">The connection definition.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The stored connection (masked).</returns>
    public Task<DbConnectionDto> UpsertConnectionAsync(DbConnectionUpsertDto body, CancellationToken ct = default)
        => ApiHttp.SendAsync<DbConnectionDto>(
            this.http,
            new HttpRequestMessage(HttpMethod.Post, "api/database/connections/")
            {
                Content = JsonContent.Create(body, options: ApiHttp.Json),
            },
            ct);

    /// <summary>Deletes a named connection (<c>DELETE /api/database/connections/{id}</c>; 404 tolerated)~ 🗑️.</summary>
    /// <param name="connectionId">The connection id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task DeleteConnectionAsync(string connectionId, CancellationToken ct = default)
    {
        try
        {
            await ApiHttp.SendNoContentAsync(
                this.http,
                new HttpRequestMessage(HttpMethod.Delete, $"api/database/connections/{Uri.EscapeDataString(connectionId)}"),
                ct).ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.Error.StatusCode == 404)
        {
            // Deleting an already-absent connection is fine~
        }
    }

    /// <summary>Lists a connection's catalogued tables (<c>GET /catalog/{connectionId}</c>)~ 📚.</summary>
    /// <param name="connectionId">The named connection id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The catalogued tables.</returns>
    public Task<List<LinqTableDto>> ListCatalogAsync(string connectionId, CancellationToken ct = default)
        => ApiHttp.SendAsync<List<LinqTableDto>>(
            this.http,
            new HttpRequestMessage(HttpMethod.Get, $"api/database/catalog/{Uri.EscapeDataString(connectionId)}"),
            ct);

    /// <summary>Introspects the live connection into the catalog (<c>POST /catalog/{id}/import</c>)~ 📥.</summary>
    /// <param name="connectionId">The named connection id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The import summary.</returns>
    public Task<LinqCatalogImportResponseDto> ImportCatalogAsync(string connectionId, CancellationToken ct = default)
        => ApiHttp.SendAsync<LinqCatalogImportResponseDto>(
            this.http,
            new HttpRequestMessage(HttpMethod.Post, $"api/database/catalog/{Uri.EscapeDataString(connectionId)}/import"),
            ct);

    /// <summary>Manually defines/updates a table (<c>PUT /catalog/{id}/{table}</c>)~ 💾.</summary>
    /// <param name="connectionId">The named connection id.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="body">The table definition.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The stored table.</returns>
    public Task<LinqTableDto> UpsertTableAsync(string connectionId, string tableName, LinqTableUpsertDto body, CancellationToken ct = default)
        => ApiHttp.SendAsync<LinqTableDto>(
            this.http,
            new HttpRequestMessage(HttpMethod.Put, $"api/database/catalog/{Uri.EscapeDataString(connectionId)}/{Uri.EscapeDataString(tableName)}")
            {
                Content = JsonContent.Create(body, options: ApiHttp.Json),
            },
            ct);

    /// <summary>Removes a catalogued table (<c>DELETE /catalog/{id}/{table}</c>; 404 is tolerated)~ 🗑️.</summary>
    /// <param name="connectionId">The named connection id.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task RemoveTableAsync(string connectionId, string tableName, CancellationToken ct = default)
    {
        try
        {
            await ApiHttp.SendNoContentAsync(
                this.http,
                new HttpRequestMessage(
                    HttpMethod.Delete,
                    $"api/database/catalog/{Uri.EscapeDataString(connectionId)}/{Uri.EscapeDataString(tableName)}"),
                ct).ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.Error.StatusCode == 404)
        {
            // Removing an already-absent table is fine (idempotent from the Studio's view)~
        }
    }
}
