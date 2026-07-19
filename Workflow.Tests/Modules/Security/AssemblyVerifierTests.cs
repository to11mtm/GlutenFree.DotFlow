// <copyright file="AssemblyVerifierTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Security;

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
using Workflow.Modules.Security;
using Workflow.Tests.SampleModules;
using Xunit;

/// <summary>
/// 🔏 Phase 2.8.4 — Tests for <see cref="StrongNameVerifier"/> and the installer signing policy~ ✨.
/// </summary>
public sealed class AssemblyVerifierTests : IDisposable
{
    // A framework assembly is strong-name signed; the sample modules DLL is not.
    private static readonly string SignedAssembly = typeof(object).Assembly.Location;
    private static readonly string UnsignedAssembly = typeof(SampleLogModule).Assembly.Location;

    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), "wfmod-sec-" + Guid.NewGuid().ToString("N"));

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

    [Fact]
    public void Verify_SignedAssembly_ReportsToken()
    {
        var result = new StrongNameVerifier().Verify(SignedAssembly);

        result.Signed.Should().BeTrue();
        result.PublicKeyToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Verify_UnsignedAssembly_ReportsUnsigned()
    {
        var result = new StrongNameVerifier().Verify(UnsignedAssembly);

        result.Signed.Should().BeFalse();
        result.Messages.Should().Contain(m => m.Contains("not strong-name"));
    }

    [Fact]
    public void Verify_TrustedToken_Trusted()
    {
        var token = new StrongNameVerifier().Verify(SignedAssembly).PublicKeyToken!;
        var verifier = new StrongNameVerifier(new[] { token });

        verifier.Verify(SignedAssembly).Trusted.Should().BeTrue();
    }

    [Fact]
    public void Verify_UnknownToken_Untrusted()
    {
        var verifier = new StrongNameVerifier(new[] { "deadbeefdeadbeef" });

        var result = verifier.Verify(SignedAssembly);
        result.Signed.Should().BeTrue();
        result.Trusted.Should().BeFalse();
    }

    [Fact]
    public async Task Policy_Default_WarnsButLoads()
    {
        var (installer, registry) = this.NewInstaller(requireSigned: false);
        var result = await installer.InstallAsync(BuildRealPackage("sample.log", "1.0.0"));

        result.Success.Should().BeTrue(string.Join("; ", result.Errors));
        registry.HasModule("sample.log").Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Contains("not strong-name"));
    }

    [Fact]
    public async Task Policy_RequireSigned_Blocks()
    {
        var (installer, registry) = this.NewInstaller(requireSigned: true);
        var result = await installer.InstallAsync(BuildRealPackage("sample.log", "1.0.0"));

        result.Success.Should().BeFalse();
        result.Reason.Should().Be(ModuleInstallFailureReason.Untrusted);
        registry.HasModule("sample.log").Should().BeFalse();
    }

    private (ModulePackageInstaller Installer, InMemoryModuleRegistry Registry) NewInstaller(bool requireSigned)
    {
        var registry = new InMemoryModuleRegistry();
        var loader = new AssemblyModuleLoader(registry);
        var options = new ModulePackagingOptions { PackagesPath = this.tempRoot, RequireSigned = requireSigned };
        var installer = new ModulePackageInstaller(loader, registry, options, verifier: new StrongNameVerifier());
        return (installer, registry);
    }

    private static byte[] BuildRealPackage(string id, string version)
    {
        var dllBytes = File.ReadAllBytes(UnsignedAssembly);
        var dllName = Path.GetFileName(UnsignedAssembly);
        var entry = $"lib/{dllName}";
        var manifest = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["version"] = version,
            ["displayName"] = id,
            ["entryAssembly"] = entry,
        });

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "module.json", Encoding.UTF8.GetBytes(manifest));
            WriteEntry(archive, entry, dllBytes);
            var depsPath = Path.ChangeExtension(UnsignedAssembly, ".deps.json");
            if (File.Exists(depsPath))
            {
                WriteEntry(archive, $"lib/{Path.GetFileName(depsPath)}", File.ReadAllBytes(depsPath));
            }
        }

        return ms.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name);
        using var s = entry.Open();
        s.Write(content, 0, content.Length);
    }
}
