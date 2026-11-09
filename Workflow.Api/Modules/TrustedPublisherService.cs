// <copyright file="TrustedPublisherService.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Modules;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Workflow.Modules.Security;
using Workflow.Persistence.Abstractions;

/// <summary>
/// 🔏 Stage 7.4 — the trusted publishers in force: the server's configuration
/// (<c>Modules:Security:TrustedPublishers</c>, which the UI can't change) plus the ones saved from the
/// Trusted publishers page. Saves are validated and audited. See <c>docs/module-signing.md</c>.
/// </summary>
public sealed class TrustedPublisherService : ITrustedPublisherProvider
{
    /// <summary>The most audit entries kept.</summary>
    public const int MaxAuditEntries = 200;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly TrustedPublisherOptions host;
    private readonly bool? hostCheckRevocation;
    private readonly bool requireSigned;
    private readonly ITrustedPublisherPersistence persistence;
    private readonly Func<DateTimeOffset> clock;
    private readonly SemaphoreSlim gate = new(1, 1);
    private volatile StoredDocument document = new();

    /// <summary>Initializes a new instance of the <see cref="TrustedPublisherService"/> class.</summary>
    /// <param name="host">The server's configured publishers (locked).</param>
    /// <param name="hostCheckRevocation">The server's <c>CheckRevocation</c>, or null when it doesn't set it (then the page can).</param>
    /// <param name="requireSigned">Whether <c>Modules:Security:RequireSigned</c> is on (shown on the page).</param>
    /// <param name="persistence">Where the saved publishers and the audit live.</param>
    /// <param name="clock">Clock (tests).</param>
    public TrustedPublisherService(
        TrustedPublisherOptions? host,
        bool? hostCheckRevocation,
        bool requireSigned,
        ITrustedPublisherPersistence persistence,
        Func<DateTimeOffset>? clock = null)
    {
        this.host = host ?? new TrustedPublisherOptions();
        this.hostCheckRevocation = hostCheckRevocation;
        this.requireSigned = requireSigned;
        this.persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc/>
    public TrustedPublisherSet Current
    {
        get
        {
            var saved = this.document.Saved ?? new TrustedPublishersSave();
            return new TrustedPublisherSet(
                this.host.Certificates.Concat(saved.Certificates ?? []).ToList(),
                this.host.RootCertificates.Concat(saved.RootCertificates ?? []).ToList(),
                this.hostCheckRevocation ?? saved.CheckRevocation ?? false);
        }
    }

    /// <summary>Loads (or reloads) the saved publishers.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var json = await this.persistence.ReadAsync(ct).ConfigureAwait(false);
        this.document = string.IsNullOrWhiteSpace(json)
            ? new StoredDocument()
            : JsonSerializer.Deserialize<StoredDocument>(json, Json) ?? new StoredDocument();
    }

    /// <summary>What the Trusted publishers page shows.</summary>
    /// <returns>The view.</returns>
    public TrustedPublishersView View()
    {
        var saved = this.document.Saved ?? new TrustedPublishersSave();
        return new TrustedPublishersView(
            this.host.Certificates.Select(c => Describe(c, locked: true))
                .Concat((saved.Certificates ?? []).Select(c => Describe(c, locked: false))).ToList(),
            this.host.RootCertificates.Select(c => Describe(c, locked: true))
                .Concat((saved.RootCertificates ?? []).Select(c => Describe(c, locked: false))).ToList(),
            this.Current.CheckRevocation,
            this.hostCheckRevocation is not null,
            this.requireSigned);
    }

    /// <summary>Gets the audit trail, newest first.</summary>
    /// <returns>The entries.</returns>
    public IReadOnlyList<TrustedPublisherAuditEntry> Audit() => this.document.Audit.AsEnumerable().Reverse().ToList();

    /// <summary>Saves the publishers from the page (replacing the saved layer; the server's own stay).</summary>
    /// <param name="proposed">The new saved publishers.</param>
    /// <param name="caller">Who saved it (audit).</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Validation errors (empty = saved).</returns>
    public async Task<IReadOnlyList<string>> SaveAsync(TrustedPublishersSave proposed, string caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        var errors = this.Validate(proposed);
        if (errors.Count > 0)
        {
            return errors;
        }

        var normalized = new TrustedPublishersSave
        {
            Certificates = (proposed.Certificates ?? []).Select(Normalize).ToList(),
            RootCertificates = (proposed.RootCertificates ?? []).Select(Normalize).ToList(),
            CheckRevocation = this.hostCheckRevocation is null ? proposed.CheckRevocation : null,
        };

        await this.gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var before = this.document.Saved ?? new TrustedPublishersSave();
            var changes = Changes(before, normalized);
            var audit = this.document.Audit
                .Append(new TrustedPublisherAuditEntry(this.clock(), caller, changes.Count == 0 ? ["no changes"] : changes))
                .TakeLast(MaxAuditEntries)
                .ToList();
            var next = new StoredDocument { Saved = normalized, Audit = audit };
            await this.persistence.WriteAsync(JsonSerializer.Serialize(next, Json), ct).ConfigureAwait(false);
            this.document = next;
            return [];
        }
        finally
        {
            this.gate.Release();
        }
    }

    private List<string> Validate(TrustedPublishersSave proposed)
    {
        var errors = new List<string>();
        foreach (var (certificate, index) in (proposed.Certificates ?? []).Select((c, i) => (c, i + 1)))
        {
            var label = $"Publisher {index}{(string.IsNullOrWhiteSpace(certificate.Name) ? string.Empty : $" ('{certificate.Name}')")}";
            var hasThumbprint = !string.IsNullOrWhiteSpace(certificate.Thumbprint);
            var hasPem = !string.IsNullOrWhiteSpace(certificate.Pem);
            if (!hasThumbprint && !hasPem)
            {
                errors.Add($"{label}: give a thumbprint or the certificate (PEM).");
                continue;
            }

            if (hasThumbprint && PackageSignatureVerifier.NormalizeThumbprint(certificate.Thumbprint!).Length is not (40 or 64))
            {
                errors.Add($"{label}: the thumbprint must be a SHA-256 (64 hex digits) or SHA-1 (40 hex digits) thumbprint.");
            }

            if (hasPem)
            {
                var parsed = PackageSignatureVerifier.Load(certificate);
                if (parsed is null)
                {
                    errors.Add($"{label}: the certificate isn't valid PEM (-----BEGIN CERTIFICATE----- …).");
                }
                else if (hasThumbprint && !ThumbprintsOf(parsed).Contains(PackageSignatureVerifier.NormalizeThumbprint(certificate.Thumbprint!)))
                {
                    errors.Add($"{label}: the thumbprint doesn't match the certificate.");
                }
            }
        }

        foreach (var (root, index) in (proposed.RootCertificates ?? []).Select((c, i) => (c, i + 1)))
        {
            var label = $"CA root {index}{(string.IsNullOrWhiteSpace(root.Name) ? string.Empty : $" ('{root.Name}')")}";
            if (PackageSignatureVerifier.Load(root) is null)
            {
                errors.Add($"{label}: a CA root needs its certificate as valid PEM (-----BEGIN CERTIFICATE----- …).");
            }
        }

        if (this.hostCheckRevocation is { } locked && proposed.CheckRevocation is { } wanted && wanted != locked)
        {
            errors.Add($"Revocation checking is set by the server's configuration ({TrustedPublisherOptions.SectionName}:CheckRevocation) and can't be changed here.");
        }

        return errors;
    }

    private static TrustedCertificate Normalize(TrustedCertificate certificate)
    {
        var parsed = PackageSignatureVerifier.Load(certificate);
        return new TrustedCertificate
        {
            Name = string.IsNullOrWhiteSpace(certificate.Name)
                ? parsed?.GetNameInfo(X509NameType.SimpleName, forIssuer: false)
                : certificate.Name.Trim(),
            Thumbprint = parsed is not null
                ? parsed.GetCertHashString(HashAlgorithmName.SHA256).ToUpperInvariant()
                : PackageSignatureVerifier.NormalizeThumbprint(certificate.Thumbprint ?? string.Empty),
            Pem = parsed?.ExportCertificatePem(),
        };
    }

    private static TrustedCertificateView Describe(TrustedCertificate certificate, bool locked)
    {
        var parsed = PackageSignatureVerifier.Load(certificate);
        return new TrustedCertificateView(
            certificate.Name ?? parsed?.GetNameInfo(X509NameType.SimpleName, forIssuer: false),
            parsed is not null
                ? parsed.GetCertHashString(HashAlgorithmName.SHA256).ToUpperInvariant()
                : string.IsNullOrWhiteSpace(certificate.Thumbprint) ? null : PackageSignatureVerifier.NormalizeThumbprint(certificate.Thumbprint),
            parsed?.Subject,
            parsed is null ? null : new DateTimeOffset(parsed.NotAfter.ToUniversalTime(), TimeSpan.Zero),
            parsed?.ExportCertificatePem(),
            locked);
    }

    private static HashSet<string> ThumbprintsOf(X509Certificate2 certificate)
        => new(StringComparer.Ordinal)
        {
            certificate.GetCertHashString(HashAlgorithmName.SHA256).ToUpperInvariant(),
            certificate.Thumbprint.ToUpperInvariant(),
        };

    private static List<string> Changes(TrustedPublishersSave before, TrustedPublishersSave after)
    {
        static string Key(TrustedCertificate c) => c.Thumbprint ?? string.Empty;
        static string Label(TrustedCertificate c) => $"{c.Name ?? "(unnamed)"} ({c.Thumbprint})";

        var changes = new List<string>();
        void Diff(IList<TrustedCertificate>? old, IList<TrustedCertificate>? now, string kind)
        {
            var oldKeys = (old ?? []).ToDictionary(Key, c => c);
            var newKeys = (now ?? []).ToDictionary(Key, c => c);
            changes.AddRange(newKeys.Where(kv => !oldKeys.ContainsKey(kv.Key)).Select(kv => $"Added {kind} {Label(kv.Value)}"));
            changes.AddRange(oldKeys.Where(kv => !newKeys.ContainsKey(kv.Key)).Select(kv => $"Removed {kind} {Label(kv.Value)}"));
        }

        Diff(before.Certificates, after.Certificates, "publisher");
        Diff(before.RootCertificates, after.RootCertificates, "CA root");
        if (before.CheckRevocation != after.CheckRevocation)
        {
            changes.Add($"Revocation checking: {before.CheckRevocation?.ToString() ?? "(unset)"} → {after.CheckRevocation?.ToString() ?? "(unset)"}");
        }

        return changes;
    }

    private sealed class StoredDocument
    {
        public TrustedPublishersSave? Saved { get; set; }

        public List<TrustedPublisherAuditEntry> Audit { get; set; } = new();
    }
}

/// <summary>🔏 Stage 7.4 — the publishers saved from the page (the editable layer).</summary>
public sealed class TrustedPublishersSave
{
    /// <summary>Gets or sets the pinned publisher certificates.</summary>
    public List<TrustedCertificate>? Certificates { get; set; } = new();

    /// <summary>Gets or sets the trusted CA roots.</summary>
    public List<TrustedCertificate>? RootCertificates { get; set; } = new();

    /// <summary>Gets or sets revocation checking (ignored when the server's configuration sets it).</summary>
    public bool? CheckRevocation { get; set; }
}

/// <summary>🔏 Stage 7.4 — what the Trusted publishers page shows.</summary>
/// <param name="Certificates">Pinned publishers (the server's first, locked).</param>
/// <param name="RootCertificates">CA roots (the server's first, locked).</param>
/// <param name="CheckRevocation">Whether CA-chained certificates are checked for revocation.</param>
/// <param name="CheckRevocationLocked">Whether that's set by the server's configuration.</param>
/// <param name="RequireSigned">Whether <c>Modules:Security:RequireSigned</c> is on.</param>
public sealed record TrustedPublishersView(
    IReadOnlyList<TrustedCertificateView> Certificates,
    IReadOnlyList<TrustedCertificateView> RootCertificates,
    bool CheckRevocation,
    bool CheckRevocationLocked,
    bool RequireSigned);

/// <summary>🔏 Stage 7.4 — one trusted certificate as shown on the page.</summary>
/// <param name="Name">The display name.</param>
/// <param name="Thumbprint">The SHA-256 thumbprint (or the configured one when there's no PEM).</param>
/// <param name="Subject">The certificate subject, when known.</param>
/// <param name="NotAfter">When the certificate expires, when known.</param>
/// <param name="Pem">The certificate (public) as PEM, when known.</param>
/// <param name="Locked">Whether it comes from the server's configuration (not editable here).</param>
public sealed record TrustedCertificateView(string? Name, string? Thumbprint, string? Subject, DateTimeOffset? NotAfter, string? Pem, bool Locked);

/// <summary>🔏 Stage 7.4 — one audited change to the trusted publishers.</summary>
/// <param name="At">When.</param>
/// <param name="By">Who.</param>
/// <param name="Changes">What changed.</param>
public sealed record TrustedPublisherAuditEntry(DateTimeOffset At, string By, IReadOnlyList<string> Changes);

/// <summary>🔏 Stage 7.4 — where the saved publishers and their audit trail live.</summary>
public interface ITrustedPublisherPersistence
{
    /// <summary>Reads the stored document (null = none yet).</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The JSON.</returns>
    Task<string?> ReadAsync(CancellationToken ct = default);

    /// <summary>Writes the stored document.</summary>
    /// <param name="json">The JSON.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task.</returns>
    Task WriteAsync(string json, CancellationToken ct = default);
}

/// <summary>🔏 Stage 7.4 — keeps the saved publishers in the blob store, so every host in a cluster sees them.</summary>
public sealed class BlobTrustedPublisherPersistence : ITrustedPublisherPersistence
{
    private const string Key = "settings/trusted-publishers.json";
    private readonly IBlobStore blobs;

    /// <summary>Initializes a new instance of the <see cref="BlobTrustedPublisherPersistence"/> class.</summary>
    /// <param name="blobs">The blob store.</param>
    public BlobTrustedPublisherPersistence(IBlobStore blobs) => this.blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));

    /// <inheritdoc/>
    public async Task<string?> ReadAsync(CancellationToken ct = default)
    {
        await using var stream = await this.blobs.GetAsync(Key, ct).ConfigureAwait(false);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task WriteAsync(string json, CancellationToken ct = default)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await this.blobs.PutAsync(Key, stream, "application/json", ct).ConfigureAwait(false);
    }
}

/// <summary>🔏 Stage 7.4 — in-process persistence (no blob store; saved publishers last until restart).</summary>
public sealed class InMemoryTrustedPublisherPersistence : ITrustedPublisherPersistence
{
    private string? json;

    /// <inheritdoc/>
    public Task<string?> ReadAsync(CancellationToken ct = default) => Task.FromResult(this.json);

    /// <inheritdoc/>
    public Task WriteAsync(string json, CancellationToken ct = default)
    {
        this.json = json;
        return Task.CompletedTask;
    }
}

/// <summary>
/// 🔏 Stage 7.4 — loads the saved publishers at startup and every minute (so a change saved on one
/// host reaches the others), and warns when <c>RequireSigned</c> is on but no publisher is trusted.
/// </summary>
public sealed class TrustedPublisherHostedService : BackgroundService
{
    private readonly TrustedPublisherService service;
    private readonly ILogger<TrustedPublisherHostedService> logger;

    /// <summary>Initializes a new instance of the <see cref="TrustedPublisherHostedService"/> class.</summary>
    /// <param name="service">The publisher service.</param>
    /// <param name="logger">The logger.</param>
    public TrustedPublisherHostedService(TrustedPublisherService service, ILogger<TrustedPublisherHostedService> logger)
    {
        this.service = service;
        this.logger = logger;
    }

    /// <inheritdoc/>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await this.service.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this.logger.LogError(ex, "🔏 Couldn't load the saved trusted publishers; using the server's configuration only~");
        }

        var view = this.service.View();
        if (view.RequireSigned && view.Certificates.Count == 0 && view.RootCertificates.Count == 0)
        {
            this.logger.LogWarning(
                "🔏 Modules:Security:RequireSigned is on but no trusted publishers are configured, so only modules accepted by a strong-name token "
                + "(key pinning, not proof of authorship) can install or load. Add publishers under Settings → Trusted publishers, or in {Section}~",
                TrustedPublisherOptions.SectionName);
        }

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await this.service.LoadAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    this.logger.LogWarning(ex, "🔏 Couldn't reload the trusted publishers; keeping the last ones~");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down~
        }
    }
}
