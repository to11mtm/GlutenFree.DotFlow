// <copyright file="PackageExtraModulesFindingTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Findings.Modules;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
/// Finding 04 §9.7 — an uploaded assembly can register more module ids than its manifest declares.
/// </summary>
/// <remarks>
/// The loader discovers and registers EVERY valid <c>IWorkflowModule</c> in the entry assembly, and
/// the installer then only confirms the manifest's id is among them. So a package that declares one
/// module can register others. This builds a real <c>.wfmod</c> from the SampleModules assembly with
/// a manifest naming only <c>sample.log</c>, installs it, and asserts an UNDECLARED module
/// (<c>sample.delay</c>) is not registered. Temp files are cleaned up; no network.
/// </remarks>
[Trait("Category", FindingDefs.Category)]
public sealed class PackageExtraModulesFindingTests : IDisposable
{
    private static readonly string SampleDllPath = typeof(SampleLogModule).Assembly.Location;
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), "wfmod-finding-" + Guid.NewGuid().ToString("N"));

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
    public async Task Install_ShouldNotRegister_ModulesNotDeclaredInManifest()
    {
        var registry = new InMemoryModuleRegistry();
        var loader = new AssemblyModuleLoader(registry);
        var installer = new ModulePackageInstaller(
            loader,
            registry,
            new ModulePackagingOptions { PackagesPath = this.tempRoot });

        var package = BuildPackageDeclaringOnly("sample.log");

        var result = await installer.InstallAsync(package);
        result.Success.Should().BeTrue("the declared module installs fine; the concern is the extras");

        registry.HasModule("sample.delay").Should().BeFalse(
            because: "a package declaring only sample.log should not also register sample.delay (04 §9.7)");
    }

    private static byte[] BuildPackageDeclaringOnly(string declaredId)
    {
        var dllBytes = File.ReadAllBytes(SampleDllPath);
        var dllName = Path.GetFileName(SampleDllPath);
        var entry = $"lib/{dllName}";

        var manifest = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = declaredId,
            ["version"] = "1.0.0",
            ["displayName"] = declaredId,
            ["entryAssembly"] = entry,
        });

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "module.json", Encoding.UTF8.GetBytes(manifest));
            WriteEntry(archive, entry, dllBytes);

            var depsPath = Path.ChangeExtension(SampleDllPath, ".deps.json");
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
