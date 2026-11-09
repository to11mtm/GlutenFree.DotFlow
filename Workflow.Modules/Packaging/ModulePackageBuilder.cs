// <copyright file="ModulePackageBuilder.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Packaging;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Workflow.Modules.Security;

/// <summary>
/// 📦🔏 Stage 7.4 — builds a <c>.wfmod</c> from a folder: writes the SHA-256 of every file into
/// <c>module.json</c>'s <c>contentHashes</c>, optionally signs <c>module.json</c> (a detached CMS
/// signature in <c>signature.p7s</c>), and zips it. Used by the <c>dotflow-module</c> CLI.
/// </summary>
public static class ModulePackageBuilder
{
    /// <summary>Builds a package from a folder holding <c>module.json</c> and the module's files.</summary>
    /// <param name="folder">The folder.</param>
    /// <param name="signer">The publisher's certificate, with its private key; null = unsigned.</param>
    /// <param name="extraCertificates">Intermediate certificates to include in the signature (optional).</param>
    /// <returns>The result.</returns>
    public static ModulePackageBuildResult Build(string folder, X509Certificate2? signer = null, IEnumerable<X509Certificate2>? extraCertificates = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        var root = Path.GetFullPath(folder);
        var manifestPath = Path.Combine(root, ModulePackageReader.ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return ModulePackageBuildResult.Fail($"'{ModulePackageReader.ManifestFileName}' was not found in '{root}'.");
        }

        if (JsonNode.Parse(File.ReadAllText(manifestPath)) is not JsonObject manifest)
        {
            return ModulePackageBuildResult.Fail($"'{ModulePackageReader.ManifestFileName}' must be a JSON object.");
        }

        if (signer is not null && !signer.HasPrivateKey)
        {
            return ModulePackageBuildResult.Fail("The signing certificate has no private key.");
        }

        // Every file except the manifest, a previous signature and an install record~
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ModulePackageReader.ManifestFileName,
            ModulePackageReader.SignatureFileName,
            ModuleInstallRecord.FileName,
        };
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => (Full: f, Relative: Path.GetRelativePath(root, f).Replace('\\', '/')))
            .Where(f => !excluded.Contains(f.Relative))
            .OrderBy(f => f.Relative, StringComparer.Ordinal)
            .ToList();

        var hashes = new JsonObject();
        foreach (var (full, relative) in files)
        {
            using var stream = File.OpenRead(full);
            hashes[relative] = Convert.ToBase64String(SHA256.HashData(stream));
        }

        manifest["contentHashes"] = hashes;
        var manifestBytes = Encoding.UTF8.GetBytes(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        // The result must be a package the server accepts~
        var parsed = JsonSerializer.Deserialize<ModuleManifest>(manifestBytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var validation = parsed?.Validate();
        if (parsed is null || validation is null || !validation.IsValid)
        {
            return ModulePackageBuildResult.Fail(validation?.Errors.Select(e => e.ToString()).ToArray() ?? ["The manifest is empty."]);
        }

        if (!files.Any(f => string.Equals(f.Relative, parsed.EntryAssembly.Replace('\\', '/').TrimStart('/'), StringComparison.OrdinalIgnoreCase)))
        {
            return ModulePackageBuildResult.Fail($"The entry assembly '{parsed.EntryAssembly}' isn't in the folder.");
        }

        byte[]? signature = null;
        if (signer is not null)
        {
            var cms = new SignedCms(new ContentInfo(manifestBytes), detached: true);
            var cmsSigner = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, signer)
            {
                IncludeOption = X509IncludeOption.EndCertOnly,
                DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"), // SHA-256
            };
            foreach (var extra in extraCertificates ?? [])
            {
                cmsSigner.Certificates.Add(extra);
            }

            cms.ComputeSignature(cmsSigner, silent: true);
            signature = cms.Encode();
        }

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, ModulePackageReader.ManifestFileName, manifestBytes);
            if (signature is not null)
            {
                Write(archive, ModulePackageReader.SignatureFileName, signature);
            }

            foreach (var (full, relative) in files)
            {
                Write(archive, relative, File.ReadAllBytes(full));
            }
        }

        return new ModulePackageBuildResult(
            true,
            buffer.ToArray(),
            parsed,
            files.Select(f => f.Relative).ToList(),
            signer?.GetNameInfo(X509NameType.SimpleName, forIssuer: false),
            signer?.GetCertHashString(HashAlgorithmName.SHA256).ToUpperInvariant(),
            Array.Empty<string>());
    }

    private static void Write(ZipArchive archive, string name, byte[] content)
    {
        using var stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
        stream.Write(content, 0, content.Length);
    }
}

/// <summary>📦🔏 Stage 7.4 — the outcome of building a package.</summary>
/// <param name="Success">Whether it was built.</param>
/// <param name="Package">The <c>.wfmod</c> bytes.</param>
/// <param name="Manifest">The final manifest.</param>
/// <param name="Files">The files included (besides <c>module.json</c> and <c>signature.p7s</c>).</param>
/// <param name="Publisher">The signer's name, when signed.</param>
/// <param name="Thumbprint">The signer's SHA-256 thumbprint, when signed.</param>
/// <param name="Errors">Why it couldn't be built.</param>
public sealed record ModulePackageBuildResult(
    bool Success,
    byte[] Package,
    ModuleManifest? Manifest,
    IReadOnlyList<string> Files,
    string? Publisher,
    string? Thumbprint,
    IReadOnlyList<string> Errors)
{
    /// <summary>Creates a failed result.</summary>
    /// <param name="errors">The errors.</param>
    /// <returns>The result.</returns>
    public static ModulePackageBuildResult Fail(params string[] errors)
        => new(false, Array.Empty<byte>(), null, Array.Empty<string>(), null, null, errors);
}
