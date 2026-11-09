// <copyright file="ModulePackageInstaller.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Packaging;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Loading;
using Workflow.Modules.Security;

/// <summary>
/// 🏗️ Phase 2.8.0 — Installs/uninstalls <c>.wfmod</c> packages: validates via
/// <see cref="ModulePackageReader"/>, gates on engine version, extracts to a content-addressed
/// directory, loads through <see cref="IModuleLoader"/>, and rolls back on failure~ ✨.
/// </summary>
public sealed class ModulePackageInstaller : IModuleLoadGate
{
    private readonly IModuleLoader loader;
    private readonly IModuleRegistry registry;
    private readonly ModulePackageReader reader;
    private readonly ModulePackagingOptions options;
    private readonly IModulePackageArchive? archive;
    private readonly IAssemblyVerifier? verifier;
    private readonly PackageSignatureVerifier? signatureVerifier;
    private readonly ILogger logger;

    // 🔏 Stage 7.4 — the trust of each installed version (by its directory), as last checked~
    private readonly ConcurrentDictionary<string, ModuleTrustInfo> trustByDirectory = new(StringComparer.OrdinalIgnoreCase);

    private static readonly System.Text.Json.JsonSerializerOptions RecordJson = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>Initializes a new instance of the <see cref="ModulePackageInstaller"/> class~ 🏗️.</summary>
    /// <param name="loader">The assembly module loader.</param>
    /// <param name="registry">The module registry.</param>
    /// <param name="options">Packaging options (paths, size cap, engine version).</param>
    /// <param name="reader">Optional package reader (a default is created when null).</param>
    /// <param name="archive">Optional package-archival seam (Q1).</param>
    /// <param name="verifier">Optional assembly signature verifier (Phase 2.8.4).</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="signatureVerifier">
    /// 🔏 Stage 7.4 — optional package signature verifier (trusted publishers). Without it, a signed
    /// package is treated as signed by an untrusted publisher.
    /// </param>
    public ModulePackageInstaller(
        IModuleLoader loader,
        IModuleRegistry registry,
        ModulePackagingOptions options,
        ModulePackageReader? reader = null,
        IModulePackageArchive? archive = null,
        IAssemblyVerifier? verifier = null,
        ILogger<ModulePackageInstaller>? logger = null,
        PackageSignatureVerifier? signatureVerifier = null)
    {
        this.signatureVerifier = signatureVerifier;
        this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.reader = reader ?? new ModulePackageReader();
        this.archive = archive;
        this.verifier = verifier;
        this.logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <summary>Installs a package from its raw bytes~ 🏗️.</summary>
    /// <param name="packageBytes">The full <c>.wfmod</c> bytes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="ModuleInstallResult"/> describing the outcome.</returns>
    public async Task<ModuleInstallResult> InstallAsync(byte[] packageBytes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(packageBytes);

        if (packageBytes.LongLength > this.options.MaxPackageBytes)
        {
            return ModuleInstallResult.Fail(
                ModuleInstallFailureReason.InvalidPackage,
                $"Package exceeds the maximum allowed size of {this.options.MaxPackageBytes} bytes.");
        }

        var read = this.reader.Read(packageBytes);
        if (!read.Success || read.Manifest is null)
        {
            return ModuleInstallResult.Fail(ModuleInstallFailureReason.InvalidPackage, read.Errors.ToArray());
        }

        var manifest = read.Manifest;
        var version = manifest.ParseVersion()!;
        var warnings = new List<string>(read.Warnings);

        // 🔏 Stage 7.4 — a signature that doesn't match is tampering: refused whatever the policy~
        var signature = this.VerifySignature(read.ManifestBytes, read.SignatureBytes, checkTime: true);
        if (signature.Status == PackageSignatureStatus.Invalid)
        {
            return ModuleInstallResult.Fail(ModuleInstallFailureReason.InvalidPackage, signature.Messages.ToArray());
        }

        // 🏷️ Engine version gate (SemVer) — Q6~
        if (this.EngineTooOld(manifest, out var engineError))
        {
            return ModuleInstallResult.Fail(ModuleInstallFailureReason.EngineTooOld, engineError!);
        }

        // 🚫 Duplicate same-version install~
        var targetDir = this.VersionDirectory(manifest.Id, version);
        if (Directory.Exists(targetDir) && Directory.EnumerateFileSystemEntries(targetDir).Any())
        {
            return ModuleInstallResult.Fail(
                ModuleInstallFailureReason.DuplicateVersion,
                $"Module '{manifest.Id}' version {version} is already installed.");
        }

        // 🔗 Dependency pre-check — id presence + manifest version-range satisfaction (2.8.1)~
        var unsatisfied = new List<string>();
        foreach (var dep in manifest.Dependencies)
        {
            if (string.IsNullOrWhiteSpace(dep.Id))
            {
                continue;
            }

            if (!this.registry.HasModule(dep.Id))
            {
                unsatisfied.Add(dep.Id);
                continue;
            }

            // When a version range is declared, at least one installed version must satisfy it~
            if ((dep.MinVersion is not null || dep.MaxVersion is not null)
                && !this.registry.GetModuleVersions(dep.Id).Any(dep.IsSatisfiedBy))
            {
                unsatisfied.Add($"{dep.Id} ({RangeText(dep)})");
            }
        }

        if (unsatisfied.Count > 0)
        {
            return ModuleInstallResult.Fail(
                ModuleInstallFailureReason.MissingDependencies,
                $"Missing or unsatisfied dependencies: {string.Join(", ", unsatisfied)}.");
        }

        // 📂 Extract to disk~
        try
        {
            Directory.CreateDirectory(targetDir);
            ExtractTo(packageBytes, targetDir);
        }
        catch (Exception ex)
        {
            SafeDelete(targetDir);
            return ModuleInstallResult.Fail(ModuleInstallFailureReason.LoadFailed, $"Failed to extract package: {ex.Message}");
        }

        var entryDll = Path.GetFullPath(Path.Combine(targetDir, manifest.EntryAssembly.Replace('\\', '/')));
        if (!File.Exists(entryDll))
        {
            SafeDelete(targetDir);
            return ModuleInstallResult.Fail(ModuleInstallFailureReason.LoadFailed, $"Entry assembly '{manifest.EntryAssembly}' missing after extraction.");
        }

        // 🔏 Phase 2.8.4 / Stage 7.4 — trust policy: a trusted publisher's signature, or (the kept,
        // weaker option) a trusted strong-name token. Warn by default, block when RequireSigned~
        var trust = this.Decide(signature, this.verifier?.Verify(entryDll));
        if (this.options.RequireSigned && !trust.SatisfiesRequireSigned)
        {
            SafeDelete(targetDir);
            return ModuleInstallResult.Fail(
                ModuleInstallFailureReason.Untrusted,
                trust.Messages.Count > 0
                    ? trust.Messages.ToArray()
                    : new[] { "The package isn't signed by a trusted publisher and Modules:Security:RequireSigned is enabled." });
        }

        warnings.AddRange(trust.Messages);
        try
        {
            WriteInstallRecord(targetDir, trust);
        }
        catch (Exception ex)
        {
            SafeDelete(targetDir);
            return ModuleInstallResult.Fail(ModuleInstallFailureReason.LoadFailed, $"Failed to record the installed files: {ex.Message}");
        }

        // §9.7 — the module ids this package provides (manifest 'modules', or just its 'id')~
        var declaredIds = manifest.DeclaredModuleIds();

        // 🧷 Phase 2.8.2 (Q5) — Capture each module's current latest schema for a compat diff after load~
        var priorModules = declaredIds
            .Select(id => this.registry.GetModule(id))
            .Where(m => m is not null)
            .ToDictionary(m => m!.ModuleId, m => m!, StringComparer.OrdinalIgnoreCase);

        // 🔌 Load through the existing loader — registering ONLY the declared modules (§9.7)~
        var loadResult = this.loader.LoadFromAssembly(entryDll, declaredIds);
        if (!loadResult.Success)
        {
            SafeDelete(targetDir);
            return ModuleInstallResult.Fail(ModuleInstallFailureReason.LoadFailed, loadResult.Errors.ToArray());
        }

        // ✅ Verify every declared module loaded, at the manifest's version~
        var loadedById = loadResult.LoadedModules.ToDictionary(m => m.ModuleId, StringComparer.OrdinalIgnoreCase);
        var missing = declaredIds.Where(id => !loadedById.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            this.loader.UnloadAssembly(entryDll);
            SafeDelete(targetDir);
            return ModuleInstallResult.Fail(
                ModuleInstallFailureReason.ManifestMismatch,
                missing.Count == 1 && declaredIds.Count == 1
                    ? $"The package did not contain a module with id '{missing[0]}'."
                    : $"The package did not contain declared module(s): {string.Join(", ", missing)}.");
        }

        // One package release = one version: every module it provides must report it~
        var wrongVersion = declaredIds
            .Select(id => loadedById[id])
            .Where(m => m.Version != version)
            .ToList();
        if (wrongVersion.Count > 0)
        {
            this.loader.UnloadAssembly(entryDll);
            SafeDelete(targetDir);
            return ModuleInstallResult.Fail(
                ModuleInstallFailureReason.ManifestMismatch,
                wrongVersion.Count == 1 && declaredIds.Count == 1
                    ? $"Manifest version {version} does not match the loaded module version {wrongVersion[0].Version}."
                    : $"Manifest version {version} does not match: {string.Join(", ", wrongVersion.Select(m => $"{m.ModuleId} is {m.Version}"))}.");
        }

        if (loadResult.ExcludedModuleIds.Count > 0)
        {
            warnings.Add(
                $"Not registered — the entry assembly also contains module(s) the manifest doesn't declare: {string.Join(", ", loadResult.ExcludedModuleIds)}. " +
                "List them in the manifest's 'modules' to install them.");
        }

        // 🗄️ Optional archival (Q1)~
        if (this.options.ArchivePackages && this.archive is not null)
        {
            try
            {
                await this.archive.ArchiveAsync(manifest.Id, version, packageBytes, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                warnings.Add($"Package archival failed (install still succeeded): {ex.Message}");
                this.logger.LogWarning(ex, "🗄️ Failed to archive package {ModuleId} {Version}~", manifest.Id, version);
            }
        }

        // 🧷 Phase 2.8.2 (Q5) — Surface (never block) schema-compatibility warnings on version upgrades~
        foreach (var id in declaredIds)
        {
            if (priorModules.TryGetValue(id, out var prior) && prior.Version != version)
            {
                var prefix = declaredIds.Count == 1 ? string.Empty : $"{id}: ";
                foreach (var w in Workflow.Modules.Versioning.ModuleSchemaComparer.Compare(prior.Schema, loadedById[id].Schema))
                {
                    warnings.Add($"{prefix}Schema change vs v{prior.Version}: {w}");
                }
            }
        }

        this.trustByDirectory[targetDir] = trust;
        this.logger.LogInformation(
            "📦 Installed package {PackageId} {Version} ({Modules}), trust {Trust}{Publisher}~",
            manifest.Id,
            version,
            string.Join(", ", declaredIds),
            trust.Trust,
            trust.Publisher is null ? string.Empty : $" ({trust.Publisher})");
        return ModuleInstallResult.Ok(manifest.Id, version, warnings) with { ModuleIds = declaredIds, Trust = trust };
    }

    /// <summary>Uninstalls an installed package version (unload + delete)~ 🗑️.</summary>
    /// <param name="moduleId">The module id.</param>
    /// <param name="version">The version to remove; when null, the sole installed version is used.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> when a version was found and removed.</returns>
    public Task<bool> UninstallAsync(string moduleId, Version? version, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleId);

        var resolvedVersion = version ?? this.SoleInstalledVersion(moduleId);
        if (resolvedVersion is null)
        {
            return Task.FromResult(false);
        }

        var dir = this.VersionDirectory(moduleId, resolvedVersion);
        if (!Directory.Exists(dir))
        {
            return Task.FromResult(false);
        }

        var manifest = TryReadInstalledManifest(dir);
        if (manifest is not null)
        {
            var entryDll = Path.GetFullPath(Path.Combine(dir, manifest.EntryAssembly.Replace('\\', '/')));
            this.loader.UnloadAssembly(entryDll);
        }
        else if (this.registry.HasModule(moduleId))
        {
            this.registry.UnregisterModule(moduleId);
        }

        SafeDelete(dir);
        this.trustByDirectory.TryRemove(dir, out _);
        this.logger.LogInformation("🗑️ Uninstalled module {ModuleId} {Version}~", moduleId, resolvedVersion);
        return Task.FromResult(true);
    }

    /// <summary>Gets the absolute directory a specific module version installs to~ 📁.</summary>
    /// <param name="moduleId">The module id.</param>
    /// <param name="version">The version.</param>
    /// <returns>The absolute version directory path.</returns>
    public string VersionDirectory(string moduleId, Version version)
        => Path.GetFullPath(Path.Combine(this.options.PackagesPath, SanitizeSegment(moduleId), version.ToString()));

    /// <summary>
    /// Rehydrates previously-installed packages on host start: scans the packages root and loads
    /// each installed version's entry assembly into the registry~ 🔁.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of module versions (re)loaded.</returns>
    public Task<int> RehydrateAsync(CancellationToken ct = default)
    {
        var root = Path.GetFullPath(this.options.PackagesPath);
        if (!Directory.Exists(root))
        {
            return Task.FromResult(0);
        }

        var loaded = 0;
        foreach (var moduleDir in Directory.GetDirectories(root))
        {
            foreach (var versionDir in Directory.GetDirectories(moduleDir))
            {
                ct.ThrowIfCancellationRequested();

                var manifest = TryReadInstalledManifest(versionDir);
                if (manifest is null)
                {
                    continue;
                }

                var entryDll = Path.GetFullPath(Path.Combine(versionDir, manifest.EntryAssembly.Replace('\\', '/')));
                if (!File.Exists(entryDll))
                {
                    this.logger.LogWarning("🔁 Skipping rehydrate of '{ModuleId}' — entry assembly missing at '{Path}'~", manifest.Id, entryDll);
                    continue;
                }

                // 🔏 Stage 7.4 — re-check against what was verified at install~
                var check = this.CheckInstalled(versionDir, manifest, entryDll);
                if (!check.Allowed)
                {
                    this.logger.LogError(
                        "🔏 Not loading '{ModuleId}' {Version}: Modules:Security:RequireSigned is on and {Reasons}",
                        manifest.Id,
                        manifest.Version,
                        string.Join(" ", check.Messages));
                    continue;
                }

                foreach (var message in check.Messages)
                {
                    this.logger.LogWarning("🔏 '{ModuleId}' {Version}: {Message}", manifest.Id, manifest.Version, message);
                }

                var result = this.loader.LoadFromAssembly(entryDll, manifest.DeclaredModuleIds());
                if (result.Success)
                {
                    loaded += result.LoadedModules.Count;
                }
                else
                {
                    this.logger.LogWarning("🔁 Failed to rehydrate '{ModuleId}': {Errors}~", manifest.Id, string.Join("; ", result.Errors));
                }
            }
        }

        this.logger.LogInformation("🔁 Rehydrated {Count} installed module version(s) from '{Root}'~", loaded, root);
        return Task.FromResult(loaded);
    }

    /// <summary>
    /// 🔏 Stage 7.4 — the trust of an installed package version, as last checked (install, startup
    /// re-load or hot reload); null when it hasn't been checked in this process.
    /// </summary>
    /// <param name="packageId">The package id (manifest <c>id</c>).</param>
    /// <param name="version">The version.</param>
    /// <returns>The trust, or null.</returns>
    public ModuleTrustInfo? TrustFor(string packageId, Version version)
        => this.trustByDirectory.TryGetValue(this.VersionDirectory(packageId, version), out var trust) ? trust : null;

    /// <inheritdoc/>
    public ModuleLoadCheck CheckBeforeLoad(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(assemblyPath);
        var fullPath = Path.GetFullPath(assemblyPath);
        var root = Path.GetFullPath(this.options.PackagesPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        // An installed package's file: check the whole version against its install record~
        if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            var segments = fullPath.Substring(root.Length).Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 3)
            {
                var versionDir = Path.Combine(root, segments[0], segments[1]);
                if (TryReadInstalledManifest(versionDir) is { } manifest)
                {
                    var entryDll = Path.GetFullPath(Path.Combine(versionDir, manifest.EntryAssembly.Replace('\\', '/')));
                    return this.CheckInstalled(versionDir, manifest, entryDll);
                }
            }
        }

        // A loose DLL (hot-reload dev folder): only the assembly itself can be checked~
        var trust = this.Decide(PackageSignatureResult.Unsigned, this.verifier?.Verify(fullPath));
        return this.Gate(trust);
    }

    /// <summary>🔏 Stage 7.4 — re-checks an installed version against its install record.</summary>
    private ModuleLoadCheck CheckInstalled(string versionDir, ModuleManifest manifest, string entryDll)
    {
        ModuleTrustInfo trust;
        var record = TryReadInstallRecord(versionDir);
        if (record is null)
        {
            // Installed before Stage 7.4: nothing to compare with, so judge the assembly as before~
            var legacy = this.Decide(PackageSignatureResult.Unsigned, this.verifier?.Verify(entryDll));
            trust = legacy with
            {
                Messages = legacy.Messages
                    .Append("It was installed before package signing, so changes on disk can't be detected; reinstall it (signed) to verify it.")
                    .ToList(),
            };
        }
        else
        {
            var current = HashFiles(versionDir);
            var changed = record.Files.Where(f => current.TryGetValue(f.Key, out var h) && !string.Equals(h, f.Value, StringComparison.Ordinal)).Select(f => f.Key).ToList();
            var missing = record.Files.Keys.Where(k => !current.ContainsKey(k)).ToList();
            var added = current.Keys.Where(k => !record.Files.ContainsKey(k)).ToList();
            if (changed.Count + missing.Count + added.Count > 0)
            {
                var parts = new List<string>();
                if (changed.Count > 0)
                {
                    parts.Add("changed " + string.Join(", ", changed));
                }

                if (missing.Count > 0)
                {
                    parts.Add("missing " + string.Join(", ", missing));
                }

                if (added.Count > 0)
                {
                    parts.Add("added " + string.Join(", ", added));
                }

                trust = new ModuleTrustInfo(
                    ModuleTrust.ChangedOnDisk,
                    record.Publisher,
                    record.Thumbprint,
                    PackageTrustSource.None,
                    new[] { $"Its files changed on disk since it was installed ({string.Join("; ", parts)})." },
                    DateTimeOffset.UtcNow);
            }
            else
            {
                // Same files: re-check the signature and the publisher's trust (it may have been
                // removed since), but not certificate expiry~
                var manifestPath = Path.Combine(versionDir, ModulePackageReader.ManifestFileName);
                var signaturePath = Path.Combine(versionDir, ModulePackageReader.SignatureFileName);
                var signature = this.VerifySignature(
                    File.ReadAllBytes(manifestPath),
                    File.Exists(signaturePath) ? File.ReadAllBytes(signaturePath) : null,
                    checkTime: false);
                trust = this.Decide(signature, this.verifier?.Verify(entryDll));
                if (record.Trust == ModuleTrust.Verified && trust.Trust != ModuleTrust.Verified)
                {
                    trust = trust with
                    {
                        Messages = trust.Messages.Prepend($"It was verified at install as signed by '{record.Publisher}', but that publisher is no longer trusted.").ToList(),
                    };
                }
            }
        }

        this.trustByDirectory[Path.GetFullPath(versionDir)] = trust;
        return this.Gate(trust);
    }

    private ModuleLoadCheck Gate(ModuleTrustInfo trust)
        => new(!this.options.RequireSigned || trust.SatisfiesRequireSigned, trust, trust.Messages);

    private PackageSignatureResult VerifySignature(byte[] manifestBytes, byte[]? signatureBytes, bool checkTime)
    {
        if (signatureBytes is null || signatureBytes.Length == 0)
        {
            return PackageSignatureResult.Unsigned;
        }

        return this.signatureVerifier?.Verify(manifestBytes, signatureBytes, checkTime)
            ?? new PackageSignatureResult(
                PackageSignatureStatus.Untrusted,
                null,
                null,
                PackageTrustSource.None,
                new[] { "The package is signed, but package signature verification isn't configured on this server." });
    }

    /// <summary>
    /// 🔏 Stage 7.4 — the trust policy: a trusted publisher's signature verifies; otherwise a trusted
    /// strong-name token is accepted as key pinning (weaker, kept for existing setups).
    /// </summary>
    private ModuleTrustInfo Decide(PackageSignatureResult signature, AssemblyVerificationResult? assembly)
    {
        var now = DateTimeOffset.UtcNow;
        if (signature.Status == PackageSignatureStatus.Verified)
        {
            return new ModuleTrustInfo(ModuleTrust.Verified, signature.Publisher, signature.Thumbprint, signature.TrustSource, Array.Empty<string>(), now);
        }

        var messages = new List<string>();
        if (signature.Status != PackageSignatureStatus.Unsigned)
        {
            messages.AddRange(signature.Messages);
        }

        if (assembly is { Signed: true, Trusted: true })
        {
            messages.Add(
                $"Accepted by its strong-name public key token ({assembly.PublicKeyToken}) only. That's key pinning, not proof of "
                + "authorship: anyone can build an assembly with a copied public key. Sign the package to verify its publisher.");
            return new ModuleTrustInfo(ModuleTrust.KeyPinOnly, signature.Publisher, signature.Thumbprint, PackageTrustSource.None, messages, now);
        }

        if (assembly is not null)
        {
            messages.AddRange(assembly.Messages);
        }

        var level = signature.Status == PackageSignatureStatus.Untrusted ? ModuleTrust.Untrusted : ModuleTrust.Unsigned;
        return new ModuleTrustInfo(level, signature.Publisher, signature.Thumbprint, PackageTrustSource.None, messages, now);
    }

    private static void WriteInstallRecord(string versionDir, ModuleTrustInfo trust)
    {
        var record = new ModuleInstallRecord(
            1,
            trust.Trust,
            trust.Publisher,
            trust.Thumbprint,
            trust.TrustSource,
            DateTimeOffset.UtcNow,
            HashFiles(versionDir));
        File.WriteAllText(
            Path.Combine(versionDir, ModuleInstallRecord.FileName),
            System.Text.Json.JsonSerializer.Serialize(record, RecordJson));
    }

    private static ModuleInstallRecord? TryReadInstallRecord(string versionDir)
    {
        var path = Path.Combine(versionDir, ModuleInstallRecord.FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<ModuleInstallRecord>(File.ReadAllText(path), RecordJson);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Every file under the version directory (except the install record) → base64 SHA-256.</summary>
    private static Dictionary<string, string> HashFiles(string versionDir)
    {
        var root = Path.GetFullPath(versionDir);
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (string.Equals(relative, ModuleInstallRecord.FileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            hashes[relative] = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(stream));
        }

        return hashes;
    }

    private static void ExtractTo(byte[] packageBytes, string destinationRoot)
    {
        using var archive = new ZipArchive(new MemoryStream(packageBytes, writable: false), ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            // Directory entries have empty names~
            if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith('/'))
            {
                continue;
            }

            var target = PackagePath.ResolveSafe(destinationRoot, entry.FullName)
                ?? throw new InvalidOperationException($"Unsafe package entry '{entry.FullName}'.");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static void SafeDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup — a locked plugin DLL may linger until GC unloads the ALC~
        }
    }

    private static string SanitizeSegment(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }

        return value;
    }

    private static string RangeText(ModuleDependency dep)
    {
        if (dep.MinVersion is not null && dep.MaxVersion is not null)
        {
            return $"{dep.MinVersion}–{dep.MaxVersion}";
        }

        if (dep.MinVersion is not null)
        {
            return $">= {dep.MinVersion}";
        }

        return dep.MaxVersion is not null ? $"<= {dep.MaxVersion}" : "any";
    }

    private static ModuleManifest? TryReadInstalledManifest(string versionDir)
    {
        var path = Path.Combine(versionDir, ModulePackageReader.ManifestFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<ModuleManifest>(
                File.ReadAllText(path),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// §9.7 — finds an installed package by its package id (the manifest <c>id</c>)~ 📦.
    /// </summary>
    /// <param name="packageId">The package id.</param>
    /// <returns>The package and the module ids it provides, or null when not installed.</returns>
    public InstalledPackage? FindPackage(string packageId)
        => this.InstalledPackages().FirstOrDefault(p => string.Equals(p.PackageId, packageId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// §9.7 — finds the installed package that provides <paramref name="moduleId"/> as one of its
    /// declared modules (which may be a different id than the package's own)~ 🔍.
    /// </summary>
    /// <param name="moduleId">The module id.</param>
    /// <returns>The owning package, or null.</returns>
    public InstalledPackage? FindPackageDeclaring(string moduleId)
        => this.InstalledPackages().FirstOrDefault(p => p.ModuleIds.Contains(moduleId, StringComparer.OrdinalIgnoreCase));

    /// <summary>🔏 Stage 7.4 — every installed package and the module ids it provides.</summary>
    /// <returns>The packages.</returns>
    public IReadOnlyList<InstalledPackage> Packages() => this.InstalledPackages().ToList();

    private IEnumerable<InstalledPackage> InstalledPackages()
    {
        var root = Path.GetFullPath(this.options.PackagesPath);
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var moduleDir in Directory.GetDirectories(root))
        {
            var manifest = Directory.GetDirectories(moduleDir)
                .Select(TryReadInstalledManifest)
                .FirstOrDefault(m => m is not null);
            if (manifest is not null)
            {
                yield return new InstalledPackage(manifest.Id, manifest.DeclaredModuleIds());
            }
        }
    }

    private bool EngineTooOld(ModuleManifest manifest, out string? error)
    {
        error = null;
        var min = manifest.ParseMinEngineVersion();
        if (min is null || string.IsNullOrWhiteSpace(this.options.EngineVersion))
        {
            return false;
        }

        if (!Version.TryParse(this.options.EngineVersion, out var engine))
        {
            return false;
        }

        if (engine < min)
        {
            error = $"Module '{manifest.Id}' requires engine >= {min}, but the running engine is {engine}.";
            return true;
        }

        return false;
    }

    private Version? SoleInstalledVersion(string moduleId)
    {
        var moduleDir = Path.GetFullPath(Path.Combine(this.options.PackagesPath, SanitizeSegment(moduleId)));
        if (!Directory.Exists(moduleDir))
        {
            return null;
        }

        var versions = Directory.GetDirectories(moduleDir)
            .Select(Path.GetFileName)
            .Where(n => Version.TryParse(n, out _))
            .Select(n => Version.Parse(n!))
            .OrderByDescending(v => v)
            .ToList();

        return versions.Count == 1 ? versions[0] : versions.FirstOrDefault();
    }
}

/// <summary>
/// 🏗️ Phase 2.8.0 — The outcome of a package install~ ✨.
/// </summary>
/// <param name="Success">Whether the install succeeded.</param>
/// <param name="ModuleId">The installed module id (on success).</param>
/// <param name="Version">The installed version (on success).</param>
/// <param name="Errors">Fatal errors (on failure).</param>
/// <param name="Warnings">Non-fatal advisories (e.g. missing hashes, archival failure).</param>
/// <param name="Reason">The failure category for HTTP status mapping.</param>
public sealed record ModuleInstallResult(
    bool Success,
    string? ModuleId,
    Version? Version,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    ModuleInstallFailureReason Reason)
{
    /// <summary>
    /// Gets the module ids the package installed (§9.7). For a single-module package this is just
    /// <see cref="ModuleId"/>, which is then both the package id and the module id.
    /// </summary>
    public IReadOnlyList<string> ModuleIds { get; init; } = Array.Empty<string>();

    /// <summary>🔏 Stage 7.4 — gets the installed package's trust (who signed it, and whether they're trusted).</summary>
    public ModuleTrustInfo? Trust { get; init; }

    /// <summary>Creates a successful install result~ ✅.</summary>
    /// <param name="moduleId">The installed module id.</param>
    /// <param name="version">The installed version.</param>
    /// <param name="warnings">Any non-fatal warnings.</param>
    /// <returns>A successful <see cref="ModuleInstallResult"/>.</returns>
    public static ModuleInstallResult Ok(string moduleId, Version version, IReadOnlyList<string>? warnings = null)
        => new(true, moduleId, version, Array.Empty<string>(), warnings ?? Array.Empty<string>(), ModuleInstallFailureReason.None);

    /// <summary>Creates a failed install result~ ❌.</summary>
    /// <param name="reason">The failure category.</param>
    /// <param name="errors">The fatal errors.</param>
    /// <returns>A failed <see cref="ModuleInstallResult"/>.</returns>
    public static ModuleInstallResult Fail(ModuleInstallFailureReason reason, params string[] errors)
        => new(false, null, null, errors, Array.Empty<string>(), reason);
}

/// <summary>
/// 🏷️ Phase 2.8.0 — Categorizes an install failure so the HTTP layer can pick a status code~ ✨.
/// </summary>
public enum ModuleInstallFailureReason
{
    /// <summary>No failure.</summary>
    None,

    /// <summary>The package was malformed/invalid → 422.</summary>
    InvalidPackage,

    /// <summary>The engine is older than the manifest's <c>MinEngineVersion</c> → 422.</summary>
    EngineTooOld,

    /// <summary>The same id+version is already installed → 409.</summary>
    DuplicateVersion,

    /// <summary>Required dependencies are not present → 422.</summary>
    MissingDependencies,

    /// <summary>The assembly failed to load → 422.</summary>
    LoadFailed,

    /// <summary>The loaded module did not match the manifest → 422.</summary>
    ManifestMismatch,

    /// <summary>The assembly is unsigned/untrusted and signing is required → 422.</summary>
    Untrusted,
}

/// <summary>§9.7 — an installed package and the module ids it provides~ 📦.</summary>
/// <param name="PackageId">The package id (manifest <c>id</c>; also the install directory name).</param>
/// <param name="ModuleIds">The module ids it registers (manifest <c>modules</c>, or just the id).</param>
public sealed record InstalledPackage(string PackageId, IReadOnlyList<string> ModuleIds);
