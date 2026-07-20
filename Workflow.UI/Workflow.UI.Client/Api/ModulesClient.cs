// <copyright file="ModulesClient.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Api;

using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Workflow.UI.Client.Api.Dtos;

/// <summary>
/// 📦 Phase 3.3.a.0 — Typed client for module discovery (<c>/api/v1/modules</c>). Results are
/// cached in-memory because the registered module set changes rarely within a session~ ✨.
/// </summary>
public sealed class ModulesClient
{
    private readonly HttpClient http;
    private readonly Dictionary<string, ModuleDetailsDto> detailsCache = new();
    private List<ModuleSummaryDto>? listCache;

    /// <summary>Initializes a new instance of the <see cref="ModulesClient"/> class~ 📦.</summary>
    /// <param name="http">The (auth-stamped) HTTP client.</param>
    public ModulesClient(HttpClient http) => this.http = http;

    /// <summary>Lists all registered modules (cached)~ 📦.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The module summaries.</returns>
    public async Task<IReadOnlyList<ModuleSummaryDto>> ListAsync(CancellationToken ct = default)
    {
        if (this.listCache is not null)
        {
            return this.listCache;
        }

        var result = await ApiHttp.SendAsync<List<ModuleSummaryDto>>(
            this.http, new HttpRequestMessage(HttpMethod.Get, "api/v1/modules"), ct).ConfigureAwait(false);
        this.listCache = result;
        return result;
    }

    /// <summary>Gets a module's full details incl. schema (cached)~ 📐.</summary>
    /// <param name="moduleId">The module id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The details, or null if unknown.</returns>
    public async Task<ModuleDetailsDto?> GetAsync(string moduleId, CancellationToken ct = default)
    {
        if (this.detailsCache.TryGetValue(moduleId, out var cached))
        {
            return cached;
        }

        try
        {
            var result = await ApiHttp.SendAsync<ModuleDetailsDto>(
                this.http, new HttpRequestMessage(HttpMethod.Get, $"api/v1/modules/{moduleId}"), ct).ConfigureAwait(false);
            this.detailsCache[moduleId] = result;
            return result;
        }
        catch (ApiException ex) when (ex.Error.StatusCode == 404)
        {
            return null;
        }
    }

    /// <summary>Clears the caches (e.g. after a module install/uninstall)~ 🧹.</summary>
    public void InvalidateCache()
    {
        this.listCache = null;
        this.detailsCache.Clear();
    }
}
