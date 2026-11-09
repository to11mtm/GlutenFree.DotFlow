// <copyright file="OutboundHttpClient.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Api;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 🌐 Stage 7.3 — the outbound HTTP setup screen's API (<c>/api/v1/settings/outbound-http</c>).
/// See <c>docs/outbound-http.md</c>.
/// </summary>
public sealed class OutboundHttpClient
{
    private const string Base = "api/v1/settings/outbound-http";
    private readonly HttpClient http;

    /// <summary>Initializes a new instance of the <see cref="OutboundHttpClient"/> class.</summary>
    /// <param name="http">The (auth-stamped) HTTP client.</param>
    public OutboundHttpClient(HttpClient http) => this.http = http;

    /// <summary>Gets every channel's effective, saved and locked settings.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The view.</returns>
    public Task<OutboundHttpViewDto> GetAsync(CancellationToken ct = default)
        => ApiHttp.SendAsync<OutboundHttpViewDto>(this.http, new HttpRequestMessage(HttpMethod.Get, Base), ct);

    /// <summary>Saves the policy (Admin). Throws <see cref="ApiException"/> with the reasons when refused.</summary>
    /// <param name="policy">The new saved policy.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated view.</returns>
    public Task<OutboundHttpViewDto> SaveAsync(OutboundHttpSaveDto policy, CancellationToken ct = default)
        => ApiHttp.SendAsync<OutboundHttpViewDto>(
            this.http,
            new HttpRequestMessage(HttpMethod.Put, Base) { Content = JsonContent.Create(policy, options: ApiHttp.Json) },
            ct);

    /// <summary>"Would this URL be allowed?"</summary>
    /// <param name="channel"><c>Scripts</c> or <c>HttpModule</c>.</param>
    /// <param name="url">The URL.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The outcome.</returns>
    public Task<OutboundHttpCheckDto> CheckAsync(string channel, string url, CancellationToken ct = default)
        => ApiHttp.SendAsync<OutboundHttpCheckDto>(
            this.http,
            new HttpRequestMessage(HttpMethod.Post, $"{Base}/check") { Content = JsonContent.Create(new { channel, url }, options: ApiHttp.Json) },
            ct);

    /// <summary>Gets the audit trail (Admin), newest first.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The entries.</returns>
    public Task<List<OutboundHttpAuditDto>> AuditAsync(CancellationToken ct = default)
        => ApiHttp.SendAsync<List<OutboundHttpAuditDto>>(this.http, new HttpRequestMessage(HttpMethod.Get, $"{Base}/audit"), ct);
}

/// <summary>🌐 One layer of a channel's settings (every field optional).</summary>
public sealed class OutboundHttpSettingsDto
{
    /// <summary>Gets or sets the mode (<c>Unrestricted</c>, <c>PublicOnly</c>, <c>InternalOnly</c>, <c>AllowlistOnly</c>).</summary>
    public string? Mode { get; set; }

    /// <summary>Gets or sets allowed host names.</summary>
    public List<string>? AllowedHosts { get; set; }

    /// <summary>Gets or sets denied host names.</summary>
    public List<string>? DeniedHosts { get; set; }

    /// <summary>Gets or sets allowed address ranges.</summary>
    public List<string>? AllowedCidrs { get; set; }

    /// <summary>Gets or sets denied address ranges.</summary>
    public List<string>? DeniedCidrs { get; set; }

    /// <summary>Gets or sets whether cloud metadata endpoints may be reached.</summary>
    public bool? AllowCloudMetadata { get; set; }
}

/// <summary>🌐 A channel's policy in force.</summary>
/// <param name="Channel">The channel.</param>
/// <param name="Mode">The mode.</param>
/// <param name="Configured">Whether anyone set a mode (false = fail-closed PublicOnly).</param>
/// <param name="AllowedHosts">Allowed hosts.</param>
/// <param name="DeniedHosts">Denied hosts.</param>
/// <param name="AllowedCidrs">Allowed ranges.</param>
/// <param name="DeniedCidrs">Denied ranges.</param>
/// <param name="AllowCloudMetadata">Whether metadata endpoints may be reached.</param>
/// <param name="LockedFields">Fields set by the server's configuration (read-only here).</param>
public sealed record OutboundHttpEffectiveDto(
    string Channel,
    string Mode,
    bool Configured,
    List<string> AllowedHosts,
    List<string> DeniedHosts,
    List<string> AllowedCidrs,
    List<string> DeniedCidrs,
    bool AllowCloudMetadata,
    List<string> LockedFields);

/// <summary>🌐 One channel in the setup screen.</summary>
/// <param name="Channel">The channel.</param>
/// <param name="Effective">The policy in force.</param>
/// <param name="Saved">The saved layer.</param>
/// <param name="Host">The server's layer (locked).</param>
public sealed record OutboundHttpChannelDto(string Channel, OutboundHttpEffectiveDto Effective, OutboundHttpSettingsDto? Saved, OutboundHttpSettingsDto? Host);

/// <summary>🌐 The setup screen's view.</summary>
/// <param name="Channels">The channels.</param>
/// <param name="SavedDefault">The saved default layer.</param>
/// <param name="HostDefault">The server's default layer (locked).</param>
/// <param name="UnconfiguredMode">What an unconfigured channel behaves as.</param>
public sealed record OutboundHttpViewDto(List<OutboundHttpChannelDto> Channels, OutboundHttpSettingsDto? SavedDefault, OutboundHttpSettingsDto? HostDefault, string UnconfiguredMode);

/// <summary>🌐 A save request.</summary>
public sealed class OutboundHttpSaveDto
{
    /// <summary>Gets or sets the default layer.</summary>
    public OutboundHttpSettingsDto? Default { get; set; }

    /// <summary>Gets or sets the scripts channel.</summary>
    public OutboundHttpSettingsDto? Scripts { get; set; }

    /// <summary>Gets or sets the HTTP Request module channel.</summary>
    public OutboundHttpSettingsDto? HttpModule { get; set; }
}

/// <summary>🌐 One resolved address's outcome.</summary>
/// <param name="Address">The address.</param>
/// <param name="Allowed">Whether it's allowed.</param>
/// <param name="Reason">Why.</param>
public sealed record OutboundHttpAddressDto(string Address, bool Allowed, string Reason);

/// <summary>🌐 "Would this be allowed?" outcome.</summary>
/// <param name="Allowed">Whether the request would go through.</param>
/// <param name="Reason">The deciding rule.</param>
/// <param name="Addresses">Per-address outcomes.</param>
public sealed record OutboundHttpCheckDto(bool Allowed, string Reason, List<OutboundHttpAddressDto> Addresses);

/// <summary>🌐 One audit entry.</summary>
/// <param name="At">When.</param>
/// <param name="By">Who.</param>
/// <param name="Changes">What changed.</param>
public sealed record OutboundHttpAuditDto(DateTimeOffset At, string By, List<string> Changes);
