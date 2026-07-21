// <copyright file="ModuleManagementEndpointsTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.V1;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
/// 📦🌐 Phase 2.8.5 — Integration tests for the module management endpoints (upload/enable/disable/uninstall)~ ✨.
/// </summary>
public sealed class ModuleManagementEndpointsTests
{
    private static readonly string SampleDllPath = typeof(SampleLogModule).Assembly.Location;

    [Fact]
    public async Task Upload_ValidPackage_201WithDetails()
    {
        using var factory = new ModuleFactory();
        var client = factory.CreateClient();

        var resp = await UploadAsync(client, BuildRealPackage("sample.log", "1.0.0"));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("module").GetProperty("id").GetString().Should().Be("sample.log");

        factory.Services.GetRequiredService<IModuleRegistry>().HasModule("sample.log").Should().BeTrue();
    }

    [Fact]
    public async Task Upload_InvalidZip_422()
    {
        using var factory = new ModuleFactory();
        var client = factory.CreateClient();

        var resp = await UploadAsync(client, Encoding.UTF8.GetBytes("not a zip"));

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Upload_MissingManifest_422()
    {
        using var factory = new ModuleFactory();
        var client = factory.CreateClient();
        var bytes = BuildZip(e => e["lib/x.dll"] = new byte[] { 1, 2, 3 });

        var resp = await UploadAsync(client, bytes);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Upload_DuplicateVersion_409()
    {
        using var factory = new ModuleFactory();
        var client = factory.CreateClient();
        var pkg = BuildRealPackage("sample.log", "1.0.0");
        (await UploadAsync(client, pkg)).StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await UploadAsync(client, pkg);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Enable_Disable_TogglesRegistryState()
    {
        using var factory = new ModuleFactory();
        var client = factory.CreateClient();
        await UploadAsync(client, BuildRealPackage("sample.log", "1.0.0"));

        var disable = await client.PostAsync("/api/v1/modules/sample.log/disable", null);
        disable.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Services.GetRequiredService<IModuleRegistry>().IsModuleEnabled("sample.log", new Version(1, 0, 0)).Should().BeFalse();

        var enable = await client.PostAsync("/api/v1/modules/sample.log/enable", null);
        enable.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Services.GetRequiredService<IModuleRegistry>().IsModuleEnabled("sample.log", new Version(1, 0, 0)).Should().BeTrue();
    }

    [Fact]
    public async Task EnableUnknown_404()
    {
        using var factory = new ModuleFactory();
        var client = factory.CreateClient();

        var resp = await client.PostAsync("/api/v1/modules/nope.nope/enable", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Uninstall_RemovesModule()
    {
        using var factory = new ModuleFactory();
        var client = factory.CreateClient();
        await UploadAsync(client, BuildRealPackage("sample.log", "1.0.0"));

        var resp = await client.DeleteAsync("/api/v1/modules/sample.log?version=1.0.0");

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        factory.Services.GetRequiredService<IModuleRegistry>().HasModule("sample.log").Should().BeFalse();
    }

    [Fact]
    public async Task Uninstall_BuiltinModule_409()
    {
        using var factory = new ModuleFactory();
        var client = factory.CreateClient();

        // builtin.passthrough is a builtin (non-packaged) module → cannot uninstall.
        var resp = await client.DeleteAsync("/api/v1/modules/builtin.passthrough");

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Uninstall_Unknown_404()
    {
        using var factory = new ModuleFactory();
        var client = factory.CreateClient();

        var resp = await client.DeleteAsync("/api/v1/modules/nope.nope");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Rehydrate_ReloadsInstalledPackageOnRestart()
    {
        var packagesPath = Path.Combine(Path.GetTempPath(), "wfmod-rehydrate-" + Guid.NewGuid().ToString("N"));
        try
        {
            // First host: install a package (extracts to the shared packages dir)~
            using (var factory1 = new ModuleFactory(packagesPath))
            {
                var client1 = factory1.CreateClient();
                (await UploadAsync(client1, BuildRealPackage("sample.log", "1.0.0"))).StatusCode.Should().Be(HttpStatusCode.Created);
            }

            // Second host over the same packages dir: rehydration should reload it on start~
            using var factory2 = new ModuleFactory(packagesPath);
            _ = factory2.CreateClient();
            factory2.Services.GetRequiredService<IModuleRegistry>().HasModule("sample.log").Should().BeTrue();
        }
        finally
        {
            try
            {
                if (Directory.Exists(packagesPath))
                {
                    Directory.Delete(packagesPath, recursive: true);
                }
            }
            catch
            {
                // best effort
            }
        }
    }

    // ---------- Helpers ----------
    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] packageBytes)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(packageBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(fileContent, "package", "module.wfmod");
        return await client.PostAsync("/api/v1/modules/upload", content);
    }

    private static byte[] BuildRealPackage(string id, string version)
    {
        var dllName = Path.GetFileName(SampleDllPath);
        var entry = $"lib/{dllName}";
        var manifest = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["version"] = version,
            ["displayName"] = id,
            ["entryAssembly"] = entry,
        });

        return BuildZip(entries =>
        {
            entries["module.json"] = Encoding.UTF8.GetBytes(manifest);
            entries[entry] = File.ReadAllBytes(SampleDllPath);
            var depsPath = Path.ChangeExtension(SampleDllPath, ".deps.json");
            if (File.Exists(depsPath))
            {
                entries[$"lib/{Path.GetFileName(depsPath)}"] = File.ReadAllBytes(depsPath);
            }
        });
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
                var e = archive.CreateEntry(name);
                using var s = e.Open();
                s.Write(content, 0, content.Length);
            }
        }

        return ms.ToArray();
    }

    private sealed class ModuleFactory : WebApplicationFactory<Program>
    {
        private readonly string packagesPath;
        private readonly bool ownsPath;

        public ModuleFactory()
        {
            this.packagesPath = Path.Combine(Path.GetTempPath(), "wfmod-api-" + Guid.NewGuid().ToString("N"));
            this.ownsPath = true;
        }

        public ModuleFactory(string packagesPath)
        {
            this.packagesPath = packagesPath;
            this.ownsPath = false;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "sqlite",
                    ["Persistence:ConnectionString"] = ":memory:",
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
                if (Directory.Exists(this.packagesPath))
                {
                    Directory.Delete(this.packagesPath, recursive: true);
                }
            }
            catch
            {
                // best effort
            }
        }
    }
}
