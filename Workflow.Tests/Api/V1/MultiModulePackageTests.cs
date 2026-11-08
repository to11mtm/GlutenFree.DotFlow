// <copyright file="MultiModulePackageTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.V1;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Modules.Abstractions;
using Workflow.Tests.SampleModules;
using Xunit;

/// <summary>
/// §9.7 — a package registers exactly the modules its manifest declares: just <c>id</c> (the
/// original single-module format) or the <c>modules</c> list. Other modules in the entry assembly are
/// skipped with a warning. Every declared module must exist and report the manifest version. A
/// module inside a multi-module package can be disabled on its own (and stays disabled across a
/// restart), but not uninstalled on its own — uninstall the package instead.
/// </summary>
/// <remarks>The SampleModules assembly contains <c>sample.log</c> and <c>sample.delay</c> (both 1.0.0).</remarks>
public sealed class MultiModulePackageTests
{
    private static readonly string SampleDllPath = typeof(SampleLogModule).Assembly.Location;

    [Fact]
    public async Task SingleModuleManifest_RegistersOnlyItsModule_AndWarnsAboutTheRest()
    {
        using var host = new ModuleHost();
        var client = host.CreateClient();

        var resp = await UploadAsync(client, Package("sample.log", "1.0.0"));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("modules").EnumerateArray().Select(e => e.GetString()).Should().Equal("sample.log");
        body.GetProperty("warnings").ToString().Should().Contain("sample.delay");
        var registry = host.Services.GetRequiredService<IModuleRegistry>();
        registry.HasModule("sample.log").Should().BeTrue();
        registry.HasModule("sample.delay").Should().BeFalse(
            because: "a package declaring only sample.log must not also register sample.delay (§9.7)");
    }

    [Fact]
    public async Task MultiModuleManifest_RegistersExactlyTheDeclaredModules()
    {
        using var host = new ModuleHost();
        var client = host.CreateClient();

        var resp = await UploadAsync(client, Package("sample.toolkit", "1.0.0", "sample.log", "sample.delay"));

        resp.StatusCode.Should().Be(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("modules").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo("sample.log", "sample.delay");
        var registry = host.Services.GetRequiredService<IModuleRegistry>();
        registry.HasModule("sample.log").Should().BeTrue();
        registry.HasModule("sample.delay").Should().BeTrue();
    }

    [Fact]
    public async Task DeclaredModuleMissingFromTheAssembly_FailsTheInstall_AndRegistersNothing()
    {
        using var host = new ModuleHost();
        var client = host.CreateClient();

        var resp = await UploadAsync(client, Package("sample.toolkit", "1.0.0", "sample.log", "sample.nope"));

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("sample.nope");
        host.Services.GetRequiredService<IModuleRegistry>().HasModule("sample.log").Should().BeFalse(
            because: "a failed install rolls back the modules it loaded");
    }

    [Fact]
    public async Task EveryDeclaredModule_MustReportTheManifestVersion()
    {
        using var host = new ModuleHost();
        var client = host.CreateClient();

        var resp = await UploadAsync(client, Package("sample.toolkit", "2.0.0", "sample.log", "sample.delay"));

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("sample.log is 1.0.0").And.Contain("sample.delay is 1.0.0");
    }

    [Fact]
    public async Task UninstallingOneModuleOfAPackage_IsRefused_WithAPointerToDisable()
    {
        using var host = new ModuleHost();
        var client = host.CreateClient();
        (await UploadAsync(client, Package("sample.toolkit", "1.0.0", "sample.log", "sample.delay"))).StatusCode.Should().Be(HttpStatusCode.Created);

        var resp = await client.DeleteAsync("/api/v1/modules/sample.delay");

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var detail = await resp.Content.ReadAsStringAsync();
        detail.Should().Contain("part of package 'sample.toolkit'").And.Contain("/api/v1/modules/sample.delay/disable");
        host.Services.GetRequiredService<IModuleRegistry>().HasModule("sample.delay").Should().BeTrue();
    }

    [Fact]
    public async Task UninstallingThePackage_RemovesAllItsModules()
    {
        using var host = new ModuleHost();
        var client = host.CreateClient();
        await UploadAsync(client, Package("sample.toolkit", "1.0.0", "sample.log", "sample.delay"));

        var resp = await client.DeleteAsync("/api/v1/modules/sample.toolkit");

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent, await resp.Content.ReadAsStringAsync());
        var registry = host.Services.GetRequiredService<IModuleRegistry>();
        registry.HasModule("sample.log").Should().BeFalse();
        registry.HasModule("sample.delay").Should().BeFalse();
    }

    [Fact]
    public async Task OneModuleOfAPackage_CanBeDisabled_AndStaysDisabledAfterARestart()
    {
        var packagesPath = Path.Combine(Path.GetTempPath(), "wfmod-multi-" + Guid.NewGuid().ToString("N"));
        var v1 = new Version(1, 0, 0);
        try
        {
            using (var first = new ModuleHost(packagesPath))
            {
                var client = first.CreateClient();
                await UploadAsync(client, Package("sample.toolkit", "1.0.0", "sample.log", "sample.delay"));

                (await client.PostAsync("/api/v1/modules/sample.delay/disable", null)).StatusCode.Should().Be(HttpStatusCode.OK);

                var registry = first.Services.GetRequiredService<IModuleRegistry>();
                registry.IsModuleEnabled("sample.delay", v1).Should().BeFalse();
                registry.IsModuleEnabled("sample.log", v1).Should().BeTrue(because: "disabling one module leaves its package siblings alone");
            }

            using var restarted = new ModuleHost(packagesPath);
            restarted.CreateClient();
            var after = restarted.Services.GetRequiredService<IModuleRegistry>();
            after.HasModule("sample.delay").Should().BeTrue(because: "rehydrate reloads the package's declared modules");
            after.IsModuleEnabled("sample.delay", v1).Should().BeFalse(because: "the disabled state is persisted");
            after.IsModuleEnabled("sample.log", v1).Should().BeTrue();
        }
        finally
        {
            try
            {
                Directory.Delete(packagesPath, recursive: true);
            }
            catch
            {
                // best effort — a plugin DLL may stay locked until its load context unloads~
            }
        }
    }

    [Fact]
    public async Task Rehydrate_RegistersOnlyTheDeclaredModules()
    {
        var packagesPath = Path.Combine(Path.GetTempPath(), "wfmod-multi-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var first = new ModuleHost(packagesPath))
            {
                await UploadAsync(first.CreateClient(), Package("sample.log", "1.0.0"));
            }

            using var restarted = new ModuleHost(packagesPath);
            restarted.CreateClient();
            var registry = restarted.Services.GetRequiredService<IModuleRegistry>();
            registry.HasModule("sample.log").Should().BeTrue();
            registry.HasModule("sample.delay").Should().BeFalse(because: "a restart must not register undeclared modules either");
        }
        finally
        {
            try
            {
                Directory.Delete(packagesPath, recursive: true);
            }
            catch
            {
                // best effort~
            }
        }
    }

    [Fact]
    public async Task Manifest_WithDuplicateDeclaredModules_IsRejected()
    {
        using var host = new ModuleHost();

        var resp = await UploadAsync(host.CreateClient(), Package("sample.toolkit", "1.0.0", "sample.log", "SAMPLE.LOG"));

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("same module id twice");
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] packageBytes)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(packageBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(file, "package", "module.wfmod");
        return await client.PostAsync("/api/v1/modules/upload", content);
    }

    /// <summary>Builds a real .wfmod around the SampleModules assembly.</summary>
    private static byte[] Package(string id, string version, params string[] modules)
    {
        var dllName = Path.GetFileName(SampleDllPath);
        var entry = $"lib/{dllName}";
        var manifest = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["version"] = version,
            ["displayName"] = id,
            ["entryAssembly"] = entry,
        };
        if (modules.Length > 0)
        {
            manifest["modules"] = modules;
        }

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "module.json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest)));
            Write(archive, entry, File.ReadAllBytes(SampleDllPath));
            var depsPath = Path.ChangeExtension(SampleDllPath, ".deps.json");
            if (File.Exists(depsPath))
            {
                Write(archive, $"lib/{Path.GetFileName(depsPath)}", File.ReadAllBytes(depsPath));
            }
        }

        return ms.ToArray();
    }

    private static void Write(ZipArchive archive, string name, byte[] content)
    {
        var e = archive.CreateEntry(name);
        using var s = e.Open();
        s.Write(content, 0, content.Length);
    }

    /// <summary>API host with its own packages folder (shareable across a restart).</summary>
    private sealed class ModuleHost : WebApplicationFactory<Program>
    {
        private readonly string packagesPath;
        private readonly bool ownsPath;

        public ModuleHost()
            : this(Path.Combine(Path.GetTempPath(), "wfmod-multi-" + Guid.NewGuid().ToString("N")), ownsPath: true)
        {
        }

        public ModuleHost(string packagesPath)
            : this(packagesPath, ownsPath: false)
        {
        }

        private ModuleHost(string packagesPath, bool ownsPath)
        {
            this.packagesPath = packagesPath;
            this.ownsPath = ownsPath;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "sqlite",
                    ["Persistence:ConnectionString"] = global::Workflow.Tests.TestSqlite.InMemory(),
                    ["Modules:PackagesPath"] = this.packagesPath,
                });
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!this.ownsPath)
            {
                return;
            }

            try
            {
                Directory.Delete(this.packagesPath, recursive: true);
            }
            catch
            {
                // best effort~
            }
        }
    }
}
