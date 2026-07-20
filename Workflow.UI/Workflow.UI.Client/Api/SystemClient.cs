// <copyright file="SystemClient.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Api;

using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 🩺 Phase 3.3.a.1 — Tiny client for the monitoring surface, used by the settings pane's
/// connection test (<c>GET /api/v1/status</c>)~ ✨.
/// </summary>
public sealed class SystemClient
{
    private readonly HttpClient http;

    /// <summary>Initializes a new instance of the <see cref="SystemClient"/> class~ 🩺.</summary>
    /// <param name="http">The (auth-stamped) HTTP client.</param>
    public SystemClient(HttpClient http) => this.http = http;

    /// <summary>Pings the API and returns its reported version, or throws <see cref="ApiException"/>~ 🟢.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The API version string (or "unknown").</returns>
    public async Task<string> PingAsync(CancellationToken ct = default)
    {
        var doc = await ApiHttp.SendAsync<JsonElement>(
            this.http, new HttpRequestMessage(HttpMethod.Get, "api/v1/status"), ct).ConfigureAwait(false);
        return doc.TryGetProperty("version", out var v) ? v.GetString() ?? "unknown" : "unknown";
    }
}
