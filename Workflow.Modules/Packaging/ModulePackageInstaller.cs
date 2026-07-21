// <copyright file="ModulePackageInstaller.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Packaging;

using System;
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
public sealed class ModulePackageInstaller
{
    private readonly IModuleLoader loader;
    private readonly IModuleRegistry registry;
    private readonly ModulePackageReader reader;
    private readonly ModulePackagingOptions options;
    private readonly IModulePackageArchive? archive;
    private readonly IAssemblyVerifier? verifier;
    private readonly ILogger logger;

    /// <summary>Initializes a new instance of the <see cref="ModulePackageInstaller"/> class~ 🏗️.</summary>
    /// <param name="loader">The assembly module loader.</param>
    /// <param name="registry">The module registry.</param>
    /// <param name="options">Packaging options (paths, size cap, engine version).</param>
    /// <param name="reader">Optional package reader (a default is created when null).</param>
    /// <param name="archive">Optional package-archival seam (Q1).</param>
    /// <param name="verifier">Optional assembly signature verifier (Phase 2.8.4).</param>
    /// <param name="logger">Optional logger.</param>
    public ModulePackageInstaller(
        IModuleLoader loader,
        IModuleRegistry registry,
        ModulePackagingOptions options,
        ModulePackageReader? reader = null,
        IModulePackageArchive? archive = null,
        IAssemblyVerifier? verifier = null,
        ILogger<ModulePackageInstaller>? logger = null)
    {
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

        // 🔏 Phase 2.8.4 — Signature policy (warn by default, block when RequireSigned)~
        if (this.verifier is not null)
        {
            var verification = this.verifier.Verify(entryDll);
            if (this.options.RequireSigned && (!verification.Signed || !verification.Trusted))
            {
                SafeDelete(targetDir);
                return ModuleInstallResult.Fail(
                    ModuleInstallFailureReason.Untrusted,
                    verification.Messages.Count > 0
                        ? verification.Messages.ToArray()
                        : new[] { "Assembly is unsigned or untrusted and Modules:Security:RequireSigned is enabled." });
            }

            warnings.AddRange(verification.Messages);
        }

        // 🧷 Phase 2.8.2 (Q5) — Capture the current latest version's schema for a compat diff after load~
        var priorModule = this.registry.GetModule(manifest.Id);

        // 🔌 Load through the existing loader~
        var loadResult = this.loader.LoadFromAssembly(entryDll);
        if (!loadResult.Success)
        {
            SafeDelete(targetDir);
            return ModuleInstallResult.Fail(ModuleInstallFailureReason.LoadFailed, loadResult.Errors.ToArray());
        }

        // ✅ Verify the loaded module matches the manifest~
        var loaded = loadResult.LoadedModules.FirstOrDefault(m => string.Equals(m.ModuleId, manifest.Id, StringComparison.OrdinalIgnoreCase));
        if (loaded is null)
        {
            this.loader.UnloadAssembly(entryDll);
            SafeDelete(targetDir);
            return ModuleInstallResult.Fail(
                ModuleInstallFailureReason.ManifestMismatch,
                $"The package did not contain a module with id '{manifest.Id}'.");
        }

        if (loaded.Version != version)
        {
            this.loader.UnloadAssembly(entryDll);
            SafeDelete(targetDir);
            return ModuleInstallResult.Fail(
                ModuleInstallFailureReason.ManifestMismatch,
                $"Manifest version {version} does not match the loaded module version {loaded.Version}.");
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
        if (priorModule is not null && priorModule.Version != version)
        {
            foreach (var w in Workflow.Modules.Versioning.ModuleSchemaComparer.Compare(priorModule.Schema, loaded.Schema))
            {
                warnings.Add($"Schema change vs v{priorModule.Version}: {w}");
            }
        }

        this.logger.LogInformation("📦 Installed module {ModuleId} {Version}~", manifest.Id, version);
        return ModuleInstallResult.Ok(manifest.Id, version, warnings);
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

                var result = this.loader.LoadFromAssembly(entryDll);
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
