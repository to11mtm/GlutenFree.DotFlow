// <copyright file="ModulePackageTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Packaging;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Workflow.Modules;
using Workflow.Modules.Loading;
using Workflow.Modules.Packaging;
using Workflow.Tests.SampleModules;
using Xunit;

/// <summary>
/// 📦 Phase 2.8.0 — Tests for the <c>.wfmod</c> manifest, reader, and installer~ ✨.
/// </summary>
public sealed class ModulePackageTests : IDisposable
{
    private static readonly string SampleDllPath = typeof(SampleLogModule).Assembly.Location;
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), "wfmod-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(this.tempRoot))
            {
                Directory.Delete(this.tempRoot, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }

    // ---------- Manifest ----------
    [Fact]
    public void Manifest_Valid_Deserializes()
    {
        var json = """
        { "id": "sample.log", "version": "1.0.0", "displayName": "Sample", "entryAssembly": "lib/x.dll" }
        """;
        var manifest = JsonSerializer.Deserialize<ModuleManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        manifest.Should().NotBeNull();
        manifest!.Id.Should().Be("sample.log");
        manifest.ParseVersion().Should().Be(new Version(1, 0, 0));
        manifest.Validate().IsValid.Should().BeTrue();
    }

    [Fact]
    public void Manifest_MissingRequiredFields_FailsValidation()
    {
        var manifest = new ModuleManifest { Id = string.Empty, Version = string.Empty, EntryAssembly = string.Empty };
        var result = manifest.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Select(e => e.Code).Should().Contain(new[] { "MP001", "MP002", "MP005" });
    }

    [Fact]
    public void Manifest_MalformedVersion_FailsValidation()
    {
        var manifest = new ModuleManifest { Id = "a", Version = "not-a-version", EntryAssembly = "lib/x.dll" };
        manifest.Validate().Errors.Should().Contain(e => e.Code == "MP003");
    }

    [Fact]
    public void Manifest_EntryAssemblyTraversal_FailsValidation()
    {
        var manifest = new ModuleManifest { Id = "a", Version = "1.0.0", EntryAssembly = "../evil.dll" };
        manifest.Validate().Errors.Should().Contain(e => e.Code == "MP006");
    }

    // ---------- Reader ----------
    [Fact]
    public void Reader_ValidPackage_Reads()
    {
        var bytes = BuildPackage("sample.log", "1.0.0", includeHashes: true);
        var result = new ModulePackageReader().Read(bytes);

        result.Success.Should().BeTrue();
        result.Manifest!.Id.Should().Be("sample.log");
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Reader_NotAZip_Fails()
    {
        var result = new ModulePackageReader().Read(Encoding.UTF8.GetBytes("i am not a zip"));
        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain("ZIP");
    }

    [Fact]
    public void Reader_MissingManifest_Fails()
    {
        var bytes = BuildZip(entries => entries["lib/x.dll"] = new byte[] { 1, 2, 3 });
        var result = new ModulePackageReader().Read(bytes);
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("manifest"));
    }

    [Fact]
    public void Reader_MissingEntryDll_Fails()
    {
        var manifest = ManifestJson("sample.log", "1.0.0", "lib/missing.dll", contentHashes: null);
        var bytes = BuildZip(entries => entries["module.json"] = Encoding.UTF8.GetBytes(manifest));
        var result = new ModulePackageReader().Read(bytes);
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("entry assembly"));
    }

    [Fact]
    public void Reader_ZipSlipEntry_Rejected()
    {
        var manifest = ManifestJson("sample.log", "1.0.0", "lib/x.dll", contentHashes: null);
        var bytes = BuildZip(entries =>
        {
            entries["module.json"] = Encoding.UTF8.GetBytes(manifest);
            entries["../escape.dll"] = new byte[] { 1 };
        });
        var result = new ModulePackageReader().Read(bytes);
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("escapes"));
    }

    [Fact]
    public void Reader_ContentHashMismatch_Rejects()
    {
        var manifest = ManifestJson("sample.log", "1.0.0", "lib/x.dll", contentHashes: new() { ["lib/x.dll"] = "d29uZ2hhc2g=" });
        var bytes = BuildZip(entries =>
        {
            entries["module.json"] = Encoding.UTF8.GetBytes(manifest);
            entries["lib/x.dll"] = new byte[] { 9, 9, 9 };
        });
        var result = new ModulePackageReader().Read(bytes);
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("mismatch"));
    }

    [Fact]
    public void Reader_ContentHashesAbsent_Warns()
    {
        var bytes = BuildPackage("sample.log", "1.0.0", includeHashes: false);
        var result = new ModulePackageReader().Read(bytes);
        result.Success.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Contains("contentHashes"));
    }

    // ---------- Installer ----------
    [Fact]
    public async Task Installer_ValidPackage_ExtractsAndLoads()
    {
        var (installer, registry) = this.NewInstaller();
        var bytes = BuildRealPackage("sample.log", "1.0.0");

        var result = await installer.InstallAsync(bytes);

        result.Success.Should().BeTrue(string.Join("; ", result.Errors));
        result.ModuleId.Should().Be("sample.log");
        registry.HasModule("sample.log").Should().BeTrue();
        Directory.Exists(Path.Combine(this.tempRoot, "sample.log", "1.0.0")).Should().BeTrue();
    }

    [Fact]
    public async Task Installer_DuplicateVersion_Refuses()
    {
        var (installer, _) = this.NewInstaller();
        var bytes = BuildRealPackage("sample.log", "1.0.0");
        (await installer.InstallAsync(bytes)).Success.Should().BeTrue();

        var second = await installer.InstallAsync(bytes);
        second.Success.Should().BeFalse();
        second.Reason.Should().Be(ModuleInstallFailureReason.DuplicateVersion);
    }

    [Fact]
    public async Task Installer_EngineVersionTooOld_Refuses()
    {
        var (installer, _) = this.NewInstaller(engineVersion: "1.0.0");
        var bytes = BuildRealPackage("sample.log", "1.0.0", minEngineVersion: "99.0.0");

        var result = await installer.InstallAsync(bytes);
        result.Success.Should().BeFalse();
        result.Reason.Should().Be(ModuleInstallFailureReason.EngineTooOld);
    }

    [Fact]
    public async Task Installer_LoadFailure_RollsBack()
    {
        var (installer, _) = this.NewInstaller();
        // A package whose entry "dll" is garbage → LoadFromAssembly fails → rollback deletes the dir.
        var manifest = ManifestJson("bad.mod", "1.0.0", "lib/bad.dll", contentHashes: null);
        var bytes = BuildZip(entries =>
        {
            entries["module.json"] = Encoding.UTF8.GetBytes(manifest);
            entries["lib/bad.dll"] = Encoding.UTF8.GetBytes("this is definitely not a PE file");
        });

        var result = await installer.InstallAsync(bytes);

        result.Success.Should().BeFalse();
        result.Reason.Should().Be(ModuleInstallFailureReason.LoadFailed);
        Directory.Exists(Path.Combine(this.tempRoot, "bad.mod", "1.0.0")).Should().BeFalse("failed install must roll back");
    }

    [Fact]
    public async Task Installer_Uninstall_RemovesModuleAndFiles()
    {
        var (installer, registry) = this.NewInstaller();
        var bytes = BuildRealPackage("sample.log", "1.0.0");
        await installer.InstallAsync(bytes);

        var removed = await installer.UninstallAsync("sample.log", new Version(1, 0, 0));

        removed.Should().BeTrue();
        registry.HasModule("sample.log").Should().BeFalse();
    }

    [Fact]
    public async Task Installer_VersionRange_SatisfiedByAvailableVersion()
    {
        var (installer, registry) = this.NewInstaller();
        registry.RegisterModule(new StubModule("dep.mod", new Version(1, 5, 0)), skipValidation: true);

        var bytes = BuildRealPackage("sample.log", "1.0.0", dependencies: new[]
        {
            new Dictionary<string, object?> { ["id"] = "dep.mod", ["minVersion"] = "1.0.0", ["maxVersion"] = "2.0.0" },
        });

        var result = await installer.InstallAsync(bytes);
        result.Success.Should().BeTrue(string.Join("; ", result.Errors));
    }

    [Fact]
    public async Task Installer_VersionRange_Unsatisfied_Fails()
    {
        var (installer, registry) = this.NewInstaller();
        registry.RegisterModule(new StubModule("dep.mod", new Version(3, 0, 0)), skipValidation: true);

        var bytes = BuildRealPackage("sample.log", "1.0.0", dependencies: new[]
        {
            new Dictionary<string, object?> { ["id"] = "dep.mod", ["minVersion"] = "1.0.0", ["maxVersion"] = "2.0.0" },
        });

        var result = await installer.InstallAsync(bytes);
        result.Success.Should().BeFalse();
        result.Reason.Should().Be(ModuleInstallFailureReason.MissingDependencies);
        result.Errors.Should().Contain(e => e.Contains("dep.mod"));
    }

    // ---------- Helpers ----------
    private (ModulePackageInstaller Installer, InMemoryModuleRegistry Registry) NewInstaller(string? engineVersion = null)
    {
        var registry = new InMemoryModuleRegistry();
        var loader = new AssemblyModuleLoader(registry);
        var options = new ModulePackagingOptions { PackagesPath = this.tempRoot, EngineVersion = engineVersion };
        return (new ModulePackageInstaller(loader, registry, options), registry);
    }

    private static byte[] BuildRealPackage(string id, string version, string? minEngineVersion = null, IReadOnlyList<Dictionary<string, object?>>? dependencies = null)
    {
        var dllBytes = File.ReadAllBytes(SampleDllPath);
        var dllName = Path.GetFileName(SampleDllPath);
        var entry = $"lib/{dllName}";
        var manifest = ManifestJson(id, version, entry, contentHashes: null, minEngineVersion: minEngineVersion, dependencies: dependencies);
        return BuildZip(entries =>
        {
            entries["module.json"] = Encoding.UTF8.GetBytes(manifest);
            entries[entry] = dllBytes;

            // Include the .deps.json beside the DLL so the ALC dependency resolver is happy~
            var depsPath = Path.ChangeExtension(SampleDllPath, ".deps.json");
            if (File.Exists(depsPath))
            {
                entries[$"lib/{Path.GetFileName(depsPath)}"] = File.ReadAllBytes(depsPath);
            }
        });
    }

    private static byte[] BuildPackage(string id, string version, bool includeHashes)
    {
        var payload = new byte[] { 4, 5, 6 };
        var entry = "lib/x.dll";
        Dictionary<string, string>? hashes = null;
        if (includeHashes)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            hashes = new Dictionary<string, string> { [entry] = Convert.ToBase64String(sha.ComputeHash(payload)) };
        }

        var manifest = ManifestJson(id, version, entry, hashes);
        return BuildZip(entries =>
        {
            entries["module.json"] = Encoding.UTF8.GetBytes(manifest);
            entries[entry] = payload;
        });
    }

    private static string ManifestJson(
        string id,
        string version,
        string entryAssembly,
        Dictionary<string, string>? contentHashes,
        string? minEngineVersion = null,
        IReadOnlyList<Dictionary<string, object?>>? dependencies = null)
    {
        var obj = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["version"] = version,
            ["displayName"] = id,
            ["entryAssembly"] = entryAssembly,
        };
        if (minEngineVersion is not null)
        {
            obj["minEngineVersion"] = minEngineVersion;
        }

        if (contentHashes is not null)
        {
            obj["contentHashes"] = contentHashes;
        }

        if (dependencies is not null)
        {
            obj["dependencies"] = dependencies;
        }

        return JsonSerializer.Serialize(obj);
    }

    private static byte[] BuildZip(Action<Dictionary<string, byte[]>> populate)
    {
        var entries = new Dictionary<string, byte[]>();
        populate(entries);

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var s = entry.Open();
                s.Write(content, 0, content.Length);
            }
        }

        return ms.ToArray();
    }
}
