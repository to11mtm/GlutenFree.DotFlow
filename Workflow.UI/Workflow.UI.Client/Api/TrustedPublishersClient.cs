// <copyright file="TrustedPublishersClient.cs" company="GlutenFree">
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
/// 🔏 Stage 7.4 — the Trusted publishers page's API (<c>/api/v1/settings/module-publishers</c>).
/// See <c>docs/module-signing.md</c>.
/// </summary>
public sealed class TrustedPublishersClient
{
    private const string Base = "api/v1/settings/module-publishers";
    private readonly HttpClient http;

    /// <summary>Initializes a new instance of the <see cref="TrustedPublishersClient"/> class.</summary>
    /// <param name="http">The (auth-stamped) HTTP client.</param>
    public TrustedPublishersClient(HttpClient http) => this.http = http;

    /// <summary>Gets the trusted publishers and CA roots (the server's ones locked).</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The view.</returns>
    public Task<TrustedPublishersViewDto> GetAsync(CancellationToken ct = default)
        => ApiHttp.SendAsync<TrustedPublishersViewDto>(this.http, new HttpRequestMessage(HttpMethod.Get, Base), ct);

    /// <summary>Saves the editable publishers (Admin). Throws <see cref="ApiException"/> with the reasons when refused.</summary>
    /// <param name="save">The editable publishers.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated view.</returns>
    public Task<TrustedPublishersViewDto> SaveAsync(TrustedPublishersSaveDto save, CancellationToken ct = default)
        => ApiHttp.SendAsync<TrustedPublishersViewDto>(
            this.http,
            new HttpRequestMessage(HttpMethod.Put, Base) { Content = JsonContent.Create(save, options: ApiHttp.Json) },
            ct);

    /// <summary>Gets the audit trail (Admin), newest first.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The entries.</returns>
    public Task<List<TrustedPublishersAuditDto>> AuditAsync(CancellationToken ct = default)
        => ApiHttp.SendAsync<List<TrustedPublishersAuditDto>>(this.http, new HttpRequestMessage(HttpMethod.Get, $"{Base}/audit"), ct);
}

/// <summary>🔏 Stage 7.4 — the Trusted publishers page's data.</summary>
/// <param name="Certificates">Pinned publishers (the server's first, locked).</param>
/// <param name="RootCertificates">CA roots (the server's first, locked).</param>
/// <param name="CheckRevocation">Whether CA-chained certificates are checked for revocation.</param>
/// <param name="CheckRevocationLocked">Whether that's set by the server's configuration.</param>
/// <param name="RequireSigned">Whether <c>Modules:Security:RequireSigned</c> is on.</param>
public sealed record TrustedPublishersViewDto(
    List<TrustedCertificateDto> Certificates,
    List<TrustedCertificateDto> RootCertificates,
    bool CheckRevocation,
    bool CheckRevocationLocked,
    bool RequireSigned);

/// <summary>🔏 Stage 7.4 — one trusted certificate.</summary>
/// <param name="Name">The display name.</param>
/// <param name="Thumbprint">The SHA-256 thumbprint (or the configured one when there's no PEM).</param>
/// <param name="Subject">The certificate subject, when known.</param>
/// <param name="NotAfter">When it expires, when known.</param>
/// <param name="Pem">The certificate (public) as PEM, when known.</param>
/// <param name="Locked">Whether it comes from the server's configuration.</param>
public sealed record TrustedCertificateDto(string? Name, string? Thumbprint, string? Subject, DateTimeOffset? NotAfter, string? Pem, bool Locked);

/// <summary>🔏 Stage 7.4 — the editable publishers sent on save.</summary>
public sealed class TrustedPublishersSaveDto
{
    /// <summary>Gets or sets the pinned publishers.</summary>
    public List<TrustedCertificateInputDto> Certificates { get; set; } = new();

    /// <summary>Gets or sets the CA roots.</summary>
    public List<TrustedCertificateInputDto> RootCertificates { get; set; } = new();

    /// <summary>Gets or sets revocation checking (ignored by the server when its configuration sets it).</summary>
    public bool? CheckRevocation { get; set; }
}

/// <summary>🔏 Stage 7.4 — a publisher or CA root as entered.</summary>
public sealed class TrustedCertificateInputDto
{
    /// <summary>Gets or sets the display name (optional; defaults to the certificate's common name).</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the SHA-256 (or SHA-1) thumbprint.</summary>
    public string? Thumbprint { get; set; }

    /// <summary>Gets or sets the certificate as PEM.</summary>
    public string? Pem { get; set; }
}

/// <summary>🔏 Stage 7.4 — one audited change.</summary>
/// <param name="At">When.</param>
/// <param name="By">Who.</param>
/// <param name="Changes">What changed.</param>
public sealed record TrustedPublishersAuditDto(DateTimeOffset At, string By, List<string> Changes);
