// <copyright file="PackageSignatureVerifier.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Security;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

/// <summary>🔏 Stage 7.4 — the outcome of checking a package's signature.</summary>
public enum PackageSignatureStatus
{
    /// <summary>The package has no <c>signature.p7s</c>.</summary>
    Unsigned,

    /// <summary>A valid signature by a trusted publisher.</summary>
    Verified,

    /// <summary>A valid signature, but the signer isn't a trusted publisher (or its certificate isn't valid).</summary>
    Untrusted,

    /// <summary>The signature doesn't match <c>module.json</c>, or can't be read: the package may have been tampered with.</summary>
    Invalid,
}

/// <summary>🔏 Stage 7.4 — why a signer is trusted.</summary>
public enum PackageTrustSource
{
    /// <summary>Not trusted.</summary>
    None,

    /// <summary>The signer's certificate is pinned in the trusted publishers.</summary>
    PinnedCertificate,

    /// <summary>The signer's certificate chains to a trusted CA root.</summary>
    CertificateAuthority,
}

/// <summary>🔏 Stage 7.4 — a package signature check result.</summary>
/// <param name="Status">The outcome.</param>
/// <param name="Publisher">The signer's name (certificate common name), when signed.</param>
/// <param name="Thumbprint">The signer certificate's SHA-256 thumbprint (upper-case hex), when signed.</param>
/// <param name="TrustSource">Why the signer is trusted.</param>
/// <param name="Messages">Explanations for anything short of <see cref="PackageSignatureStatus.Verified"/>.</param>
public sealed record PackageSignatureResult(
    PackageSignatureStatus Status,
    string? Publisher,
    string? Thumbprint,
    PackageTrustSource TrustSource,
    IReadOnlyList<string> Messages)
{
    /// <summary>Gets the result for an unsigned package.</summary>
    public static PackageSignatureResult Unsigned { get; } = new(
        PackageSignatureStatus.Unsigned, null, null, PackageTrustSource.None, new[] { "The package isn't signed." });
}

/// <summary>
/// 🔏 Stage 7.4 — verifies a module package's detached CMS (PKCS#7) signature
/// (<c>signature.p7s</c>) over the exact bytes of its <c>module.json</c>, and decides whether the
/// signer is a trusted publisher: a pinned certificate, or one that chains to a trusted CA root.
/// </summary>
/// <remarks>
/// The manifest lists the SHA-256 of every file, so a valid signature over it covers the whole
/// package (the reader checks the hashes). Works the same on Windows and Linux (no WinVerifyTrust).
/// </remarks>
public sealed class PackageSignatureVerifier
{
    private readonly ITrustedPublisherProvider publishers;

    /// <summary>Initializes a new instance of the <see cref="PackageSignatureVerifier"/> class.</summary>
    /// <param name="publishers">The trusted publishers.</param>
    public PackageSignatureVerifier(ITrustedPublisherProvider publishers)
        => this.publishers = publishers ?? throw new ArgumentNullException(nameof(publishers));

    /// <summary>Verifies a package signature.</summary>
    /// <param name="manifestBytes">The exact bytes of <c>module.json</c>.</param>
    /// <param name="signatureBytes">The bytes of <c>signature.p7s</c>, or null when absent.</param>
    /// <param name="checkTime">
    /// True at install: certificates must be within their validity period now. False when re-checking
    /// an installed package on load, so a certificate expiring later doesn't stop installed modules
    /// loading (the signature and the publisher's trust are still checked).
    /// </param>
    /// <returns>The result.</returns>
    public PackageSignatureResult Verify(byte[] manifestBytes, byte[]? signatureBytes, bool checkTime = true)
    {
        ArgumentNullException.ThrowIfNull(manifestBytes);
        if (signatureBytes is null || signatureBytes.Length == 0)
        {
            return PackageSignatureResult.Unsigned;
        }

        var cms = new SignedCms(new ContentInfo(manifestBytes), detached: true);
        try
        {
            cms.Decode(signatureBytes);
            cms.CheckSignature(verifySignatureOnly: true);
        }
        catch (CryptographicException ex)
        {
            return Invalid($"The package signature doesn't match its module.json, or can't be read ({ex.Message}). The package may have been tampered with.");
        }

        if (cms.SignerInfos.Count != 1 || cms.SignerInfos[0].Certificate is not { } signer)
        {
            return Invalid("The package signature must have exactly one signer, with its certificate included.");
        }

        var publisher = signer.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        var thumbprint = signer.GetCertHashString(HashAlgorithmName.SHA256).ToUpperInvariant();
        var set = this.publishers.Current;

        if (set.IsEmpty)
        {
            return Untrusted(publisher, thumbprint, $"Signed by '{publisher}' ({thumbprint}), but no trusted publishers are configured.");
        }

        // 📌 Pinned certificates~
        if (set.Certificates.Any(c => Matches(c, signer)))
        {
            if (checkTime && !WithinValidity(signer, out var validityProblem))
            {
                return Untrusted(publisher, thumbprint, $"Signed by trusted publisher '{publisher}', but {validityProblem}");
            }

            return new PackageSignatureResult(PackageSignatureStatus.Verified, publisher, thumbprint, PackageTrustSource.PinnedCertificate, Array.Empty<string>());
        }

        // 🏛️ CA roots~
        if (set.RootCertificates.Count > 0)
        {
            var roots = set.RootCertificates.Select(Load).Where(c => c is not null).Cast<X509Certificate2>().ToList();
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.AddRange(roots.ToArray());
            chain.ChainPolicy.ExtraStore.AddRange(cms.Certificates);
            chain.ChainPolicy.RevocationMode = set.CheckRevocation ? X509RevocationMode.Online : X509RevocationMode.NoCheck;
            if (!checkTime)
            {
                chain.ChainPolicy.VerificationFlags |= X509VerificationFlags.IgnoreNotTimeValid;
            }

            if (chain.Build(signer))
            {
                return new PackageSignatureResult(PackageSignatureStatus.Verified, publisher, thumbprint, PackageTrustSource.CertificateAuthority, Array.Empty<string>());
            }

            var problems = chain.ChainStatus
                .Where(s => s.Status != X509ChainStatusFlags.NoError)
                .Select(s => s.StatusInformation.Trim())
                .Where(s => s.Length > 0)
                .Distinct()
                .ToList();
            return Untrusted(
                publisher,
                thumbprint,
                $"Signed by '{publisher}' ({thumbprint}), which isn't a pinned publisher and doesn't chain to a trusted CA root"
                + (problems.Count > 0 ? ": " + string.Join("; ", problems) : "."));
        }

        return Untrusted(publisher, thumbprint, $"Signed by '{publisher}' ({thumbprint}), which isn't a trusted publisher.");
    }

    /// <summary>Normalizes a thumbprint for comparison (hex, upper case, no separators).</summary>
    /// <param name="thumbprint">The thumbprint.</param>
    /// <returns>The normalized thumbprint.</returns>
    public static string NormalizeThumbprint(string thumbprint)
        => new string(thumbprint.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();

    /// <summary>Parses a trusted certificate's PEM, or null when it has none or it's invalid.</summary>
    /// <param name="certificate">The trusted certificate.</param>
    /// <returns>The certificate, or null.</returns>
    public static X509Certificate2? Load(TrustedCertificate certificate)
    {
        if (string.IsNullOrWhiteSpace(certificate.Pem))
        {
            return null;
        }

        try
        {
            return X509Certificate2.CreateFromPem(certificate.Pem);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static bool Matches(TrustedCertificate trusted, X509Certificate2 signer)
    {
        if (!string.IsNullOrWhiteSpace(trusted.Thumbprint))
        {
            var wanted = NormalizeThumbprint(trusted.Thumbprint);
            if (wanted == signer.GetCertHashString(HashAlgorithmName.SHA256).ToUpperInvariant()
                || wanted == signer.Thumbprint.ToUpperInvariant())
            {
                return true;
            }
        }

        return Load(trusted) is { } pem && pem.RawData.AsSpan().SequenceEqual(signer.RawData);
    }

    private static bool WithinValidity(X509Certificate2 certificate, out string problem)
    {
        var now = DateTime.Now;
        if (now < certificate.NotBefore)
        {
            problem = $"its certificate isn't valid until {certificate.NotBefore:u}.";
            return false;
        }

        if (now > certificate.NotAfter)
        {
            problem = $"its certificate expired on {certificate.NotAfter:u}.";
            return false;
        }

        problem = string.Empty;
        return true;
    }

    private static PackageSignatureResult Invalid(string message)
        => new(PackageSignatureStatus.Invalid, null, null, PackageTrustSource.None, new[] { message });

    private static PackageSignatureResult Untrusted(string? publisher, string? thumbprint, string message)
        => new(PackageSignatureStatus.Untrusted, publisher, thumbprint, PackageTrustSource.None, new[] { message });
}
