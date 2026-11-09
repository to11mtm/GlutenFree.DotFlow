// <copyright file="ModulePackageReader.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Packaging;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

/// <summary>
/// 📖 Phase 2.8.0 — Reads and validates a <c>.wfmod</c> package (a ZIP archive) without extracting
/// it: locates + deserializes <c>module.json</c>, validates the manifest, guards against zip-slip,
/// confirms the entry assembly exists, and verifies <c>ContentHashes</c> when present~ ✨.
/// </summary>
public sealed class ModulePackageReader
{
    /// <summary>The manifest file name at the package root~ 📜.</summary>
    public const string ManifestFileName = "module.json";

    /// <summary>
    /// 🔏 Stage 7.4 — the optional detached CMS signature over the exact bytes of
    /// <see cref="ManifestFileName"/>, at the package root.
    /// </summary>
    public const string SignatureFileName = "signature.p7s";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Reads and validates a package from its raw bytes~ 📖.</summary>
    /// <param name="packageBytes">The full <c>.wfmod</c> file bytes.</param>
    /// <returns>A <see cref="ModulePackageReadResult"/> describing success/errors/warnings.</returns>
    public ModulePackageReadResult Read(byte[] packageBytes)
    {
        ArgumentNullException.ThrowIfNull(packageBytes);

        var errors = new List<string>();
        var warnings = new List<string>();

        ZipArchive archive;
        try
        {
            archive = new ZipArchive(new MemoryStream(packageBytes, writable: false), ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            return ModulePackageReadResult.Fail("The package is not a valid ZIP archive.");
        }

        using (archive)
        {
            // 🛡️ Zip-slip guard on every entry name~
            foreach (var entry in archive.Entries)
            {
                if (PackagePath.EscapesRoot(entry.FullName))
                {
                    errors.Add($"Package entry '{entry.FullName}' escapes the package root.");
                }
            }

            if (errors.Count > 0)
            {
                return ModulePackageReadResult.Fail(errors.ToArray());
            }

            var manifestEntry = archive.GetEntry(ManifestFileName)
                ?? archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, ManifestFileName, StringComparison.OrdinalIgnoreCase));
            if (manifestEntry is null)
            {
                return ModulePackageReadResult.Fail($"The package is missing its '{ManifestFileName}' manifest.");
            }

            // 🔏 Stage 7.4 — keep the exact bytes: the signature is over them~
            byte[] manifestBytes;
            using (var manifestStream = manifestEntry.Open())
            using (var buffer = new MemoryStream())
            {
                manifestStream.CopyTo(buffer);
                manifestBytes = buffer.ToArray();
            }

            ModuleManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<ModuleManifest>(manifestBytes, JsonOptions);
            }
            catch (JsonException ex)
            {
                return ModulePackageReadResult.Fail($"The manifest is not valid JSON: {ex.Message}");
            }

            if (manifest is null)
            {
                return ModulePackageReadResult.Fail("The manifest deserialized to null.");
            }

            var validation = manifest.Validate();
            if (!validation.IsValid)
            {
                return ModulePackageReadResult.Fail(validation.Errors.Select(e => e.ToString()).ToArray());
            }

            // 📦 Entry assembly must exist~
            var entryEntry = FindEntry(archive, manifest.EntryAssembly);
            if (entryEntry is null)
            {
                return ModulePackageReadResult.Fail($"The entry assembly '{manifest.EntryAssembly}' was not found in the package.");
            }

            // 🔏 Stage 7.4 — a signed package's hashes must cover every file, or the signature
            // wouldn't vouch for the uncovered ones~
            var signatureEntry = FindEntry(archive, SignatureFileName);
            byte[]? signatureBytes = null;
            if (signatureEntry is not null)
            {
                using var signatureStream = signatureEntry.Open();
                using var buffer = new MemoryStream();
                signatureStream.CopyTo(buffer);
                signatureBytes = buffer.ToArray();

                var hashed = new HashSet<string>(
                    (manifest.ContentHashes ?? new Dictionary<string, string>()).Keys.Select(NormalizePath),
                    StringComparer.OrdinalIgnoreCase);
                var uncovered = archive.Entries
                    .Where(e => !(string.IsNullOrEmpty(e.Name) && e.FullName.EndsWith('/')))
                    .Select(e => NormalizePath(e.FullName))
                    .Where(p => !string.Equals(p, ManifestFileName, StringComparison.OrdinalIgnoreCase)
                                && !string.Equals(p, SignatureFileName, StringComparison.OrdinalIgnoreCase)
                                && !hashed.Contains(p))
                    .ToList();
                if (uncovered.Count > 0)
                {
                    return ModulePackageReadResult.Fail(
                        $"The package is signed, but its manifest's 'contentHashes' don't cover: {string.Join(", ", uncovered)}. "
                        + "A signed package must list the SHA-256 of every file.");
                }
            }

            // 🔐 Content hashes: validate when present, warn when absent (Q7)~
            if (manifest.ContentHashes is { Count: > 0 })
            {
                foreach (var (relPath, expectedHash) in manifest.ContentHashes)
                {
                    var hashedEntry = FindEntry(archive, relPath);
                    if (hashedEntry is null)
                    {
                        errors.Add($"ContentHashes references missing file '{relPath}'.");
                        continue;
                    }

                    var actual = ComputeHash(hashedEntry);
                    if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add($"ContentHashes mismatch for '{relPath}' (package may be tampered).");
                    }
                }

                if (errors.Count > 0)
                {
                    return ModulePackageReadResult.Fail(errors.ToArray());
                }
            }
            else
            {
                warnings.Add("Package has no 'contentHashes' section — integrity cannot be verified. Consider adding hashes.");
            }

            return ModulePackageReadResult.Ok(manifest, warnings) with
            {
                ManifestBytes = manifestBytes,
                SignatureBytes = signatureBytes,
            };
        }
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        return archive.GetEntry(normalized)
            ?? archive.Entries.FirstOrDefault(e => string.Equals(e.FullName.Replace('\\', '/'), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string ComputeHash(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToBase64String(hash);
    }
}

/// <summary>
/// 📖 Phase 2.8.0 — The outcome of reading a <c>.wfmod</c> package~ ✨.
/// </summary>
/// <param name="Success">Whether the package is structurally valid.</param>
/// <param name="Manifest">The validated manifest (non-null on success).</param>
/// <param name="Errors">Fatal problems that made the package unreadable/invalid.</param>
/// <param name="Warnings">Non-fatal advisories (e.g. missing content hashes).</param>
public sealed record ModulePackageReadResult(
    bool Success,
    ModuleManifest? Manifest,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    /// <summary>🔏 Stage 7.4 — gets the exact bytes of <c>module.json</c> (what a signature covers).</summary>
    public byte[] ManifestBytes { get; init; } = Array.Empty<byte>();

    /// <summary>🔏 Stage 7.4 — gets the bytes of <c>signature.p7s</c>, or null when the package isn't signed.</summary>
    public byte[]? SignatureBytes { get; init; }

    /// <summary>Creates a successful read result~ ✅.</summary>
    /// <param name="manifest">The validated manifest.</param>
    /// <param name="warnings">Any non-fatal warnings.</param>
    /// <returns>A successful <see cref="ModulePackageReadResult"/>.</returns>
    public static ModulePackageReadResult Ok(ModuleManifest manifest, IReadOnlyList<string>? warnings = null)
        => new(true, manifest, Array.Empty<string>(), warnings ?? Array.Empty<string>());

    /// <summary>Creates a failed read result~ ❌.</summary>
    /// <param name="errors">The fatal errors.</param>
    /// <returns>A failed <see cref="ModulePackageReadResult"/>.</returns>
    public static ModulePackageReadResult Fail(params string[] errors)
        => new(false, null, errors, Array.Empty<string>());
}
