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

    /// <summary>🔑 Plan 08 — where the encryption keys live, or null when the server doesn't say.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The status, or null.</returns>
    public async Task<KeyStorageStatus?> GetKeyStorageAsync(CancellationToken ct = default)
    {
        var doc = await ApiHttp.SendAsync<JsonElement>(
            this.http, new HttpRequestMessage(HttpMethod.Get, "api/v1/status"), ct).ConfigureAwait(false);
        if (!doc.TryGetProperty("dataProtection", out var keys) || keys.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new KeyStorageStatus(
            keys.TryGetProperty("keyStorage", out var s) ? s.GetString() ?? "Default" : "Default",
            keys.TryGetProperty("location", out var l) ? l.GetString() ?? string.Empty : string.Empty,
            keys.TryGetProperty("warning", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() : null);
    }

    /// <summary>🌐 Stage 7.3 — the outbound HTTP channels nobody has configured (empty = all set up).</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The unconfigured channels.</returns>
    public async Task<IReadOnlyList<UnconfiguredOutboundChannel>> GetUnconfiguredOutboundHttpAsync(CancellationToken ct = default)
    {
        var doc = await ApiHttp.SendAsync<JsonElement>(
            this.http, new HttpRequestMessage(HttpMethod.Get, "api/v1/status"), ct).ConfigureAwait(false);
        if (!doc.TryGetProperty("outboundHttp", out var outbound) || outbound.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return outbound.EnumerateObject()
            .Where(c => c.Value.ValueKind == JsonValueKind.Object
                        && c.Value.TryGetProperty("configured", out var configured) && configured.ValueKind == JsonValueKind.False)
            .Select(c => new UnconfiguredOutboundChannel(c.Name, c.Value.TryGetProperty("mode", out var m) ? m.GetString() ?? "PublicOnly" : "PublicOnly"))
            .ToList();
    }
}

/// <summary>🔑 Plan 08 — where the encryption keys live (they decrypt every stored secret), and whether that's safe.</summary>
/// <param name="KeyStorage"><c>PersistenceStore</c>, <c>Folder</c> or <c>Default</c>.</param>
/// <param name="Location">Where.</param>
/// <param name="Warning">Why keys could be lost, or null.</param>
public sealed record KeyStorageStatus(string KeyStorage, string Location, string? Warning);

/// <summary>🌐 Stage 7.3 — an outbound HTTP channel nobody has configured (it runs fail-closed).</summary>
/// <param name="Channel"><c>scripts</c> or <c>httpModule</c>.</param>
/// <param name="Mode">What it runs as.</param>
public readonly record struct UnconfiguredOutboundChannel(string Channel, string Mode);

/// <summary>
/// 💾 What the host reports about streaming spill~ ✨.
/// </summary>
/// <param name="Configured">Whether a spill store is configured at all.</param>
/// <param name="Store">The configured store name (<c>None</c>, <c>Sqlite</c>, <c>Blob</c>, <c>Directory</c>).</param>
/// <param name="Encrypted">Whether spilled data is encrypted at rest.</param>
public readonly record struct SpillStatus(bool Configured, string Store, bool Encrypted);
