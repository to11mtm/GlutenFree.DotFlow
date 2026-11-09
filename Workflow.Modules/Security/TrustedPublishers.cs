// <copyright file="TrustedPublishers.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Security;

using System;
using System.Collections.Generic;

/// <summary>
/// 🔏 Stage 7.4 — the publishers whose signed module packages are trusted, bound from
/// <c>Modules:Security:TrustedPublishers</c> (server configuration).
/// </summary>
public sealed class TrustedPublisherOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Modules:Security:TrustedPublishers";

    /// <summary>Gets or sets the pinned publisher certificates (trusted as they are).</summary>
    public IList<TrustedCertificate> Certificates { get; set; } = new List<TrustedCertificate>();

    /// <summary>Gets or sets the CA roots: a publisher certificate that chains to one of these is trusted.</summary>
    public IList<TrustedCertificate> RootCertificates { get; set; } = new List<TrustedCertificate>();

    /// <summary>Gets or sets a value indicating whether certificates chained to a CA root are checked for revocation (online).</summary>
    public bool CheckRevocation { get; set; }
}

/// <summary>🔏 Stage 7.4 — one trusted certificate: by thumbprint (SHA-256 or SHA-1, hex) or as PEM.</summary>
public sealed class TrustedCertificate
{
    /// <summary>Gets or sets a display name (e.g. the publisher).</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the certificate's SHA-256 (or SHA-1) thumbprint, hex; separators are ignored.</summary>
    public string? Thumbprint { get; set; }

    /// <summary>Gets or sets the certificate as PEM (<c>-----BEGIN CERTIFICATE-----</c>…).</summary>
    public string? Pem { get; set; }
}

/// <summary>🔏 Stage 7.4 — the effective trusted publishers at a point in time.</summary>
/// <param name="Certificates">Pinned publisher certificates.</param>
/// <param name="RootCertificates">Trusted CA roots.</param>
/// <param name="CheckRevocation">Whether CA-chained certificates are checked for revocation.</param>
public sealed record TrustedPublisherSet(
    IReadOnlyList<TrustedCertificate> Certificates,
    IReadOnlyList<TrustedCertificate> RootCertificates,
    bool CheckRevocation)
{
    /// <summary>Gets an empty set (nobody trusted).</summary>
    public static TrustedPublisherSet Empty { get; } = new(Array.Empty<TrustedCertificate>(), Array.Empty<TrustedCertificate>(), false);

    /// <summary>Gets a value indicating whether any publisher or root is configured.</summary>
    public bool IsEmpty => this.Certificates.Count == 0 && this.RootCertificates.Count == 0;

    /// <summary>Creates a set from options.</summary>
    /// <param name="options">The options.</param>
    /// <returns>The set.</returns>
    public static TrustedPublisherSet From(TrustedPublisherOptions? options)
        => options is null
            ? Empty
            : new(new List<TrustedCertificate>(options.Certificates), new List<TrustedCertificate>(options.RootCertificates), options.CheckRevocation);
}

/// <summary>
/// 🔏 Stage 7.4 — supplies the current trusted publishers. Read on every verification, so a change
/// (e.g. a publisher removed in the UI) applies to the next install or load.
/// </summary>
public interface ITrustedPublisherProvider
{
    /// <summary>Gets the current trusted publishers.</summary>
    TrustedPublisherSet Current { get; }
}

/// <summary>🔏 Stage 7.4 — a fixed set of trusted publishers (server configuration only, and tests).</summary>
public sealed class StaticTrustedPublisherProvider : ITrustedPublisherProvider
{
    /// <summary>Initializes a new instance of the <see cref="StaticTrustedPublisherProvider"/> class.</summary>
    /// <param name="set">The trusted publishers.</param>
    public StaticTrustedPublisherProvider(TrustedPublisherSet set) => this.Current = set ?? TrustedPublisherSet.Empty;

    /// <inheritdoc/>
    public TrustedPublisherSet Current { get; }
}
