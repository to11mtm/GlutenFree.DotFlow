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

    /// <summary>
    /// 💾 Phase 5.1.6 — reads the host's spill capability so the designer can warn accurately~ ⚠️.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What the host reports about spill.</returns>
    /// <remarks>
    /// CopilotNote: The designer is deliberately framework-free and can't see host configuration,
    /// so spill availability and encryption ride along on <c>/status</c>. Without this the designer
    /// would have to stay silent about an unencrypted spill — and "quietly unencrypted" is exactly
    /// what Q-D rules out~ 📢
    /// </remarks>
    public async Task<SpillStatus> GetSpillStatusAsync(CancellationToken ct = default)
    {
        var doc = await ApiHttp.SendAsync<JsonElement>(
            this.http, new HttpRequestMessage(HttpMethod.Get, "api/v1/status"), ct).ConfigureAwait(false);

        if (!doc.TryGetProperty("spill", out var spill) || spill.ValueKind != JsonValueKind.Object)
        {
            // An older host that doesn't report spill — assume the safe reading rather than
            // inventing a warning about a feature that may not exist there~
            return new SpillStatus(false, "None", true);
        }

        return new SpillStatus(
            spill.TryGetProperty("configured", out var c) && c.ValueKind == JsonValueKind.True,
            spill.TryGetProperty("store", out var s) ? s.GetString() ?? "None" : "None",
            !spill.TryGetProperty("encrypted", out var e) || e.ValueKind != JsonValueKind.False);
    }
}

/// <summary>
/// 💾 What the host reports about streaming spill~ ✨.
/// </summary>
/// <param name="Configured">Whether a spill store is configured at all.</param>
/// <param name="Store">The configured store name (<c>None</c>, <c>Sqlite</c>, <c>Blob</c>, <c>Directory</c>).</param>
/// <param name="Encrypted">Whether spilled data is encrypted at rest.</param>
public readonly record struct SpillStatus(bool Configured, string Store, bool Encrypted);
