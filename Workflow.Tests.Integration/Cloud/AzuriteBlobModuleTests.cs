// <copyright file="AzuriteBlobModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Integration.Cloud;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Azurite;
using Workflow.Modules.Cloud.Builtin;
using Xunit;

/// <summary>
/// 🫐 Phase 2.5.b.1 — Azurite-backed integration tests for <see cref="AzureBlobModule"/> (Docker-gated)~ ☁️✨.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AzuriteBlobModuleTests : IAsyncLifetime
{
    private const string Container = "wf-blob-test";

    private readonly AzuriteContainer container = new AzuriteBuilder()
        .WithImage("mcr.microsoft.com/azure-storage/azurite:latest")
        .Build();

    private readonly AzureBlobModule module = new();
    private ServiceProvider services = null!;
    private string connectionString = null!;
    private string workDir = null!;

    public async Task InitializeAsync()
    {
        await this.container.StartAsync();
        this.connectionString = this.container.GetConnectionString();
        this.services = CloudModuleTestContext.BuildServices();
        this.workDir = Path.Combine(Path.GetTempPath(), "wf-blob-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.workDir);
    }

    public async Task DisposeAsync()
    {
        this.services.Dispose();
        try
        {
            Directory.Delete(this.workDir, recursive: true);
        }
        catch (IOException)
        {
        }

        await this.container.DisposeAsync();
    }

    private Dictionary<string, object?> BaseProps(string operation) => new()
    {
        ["operation"] = operation,
        ["connectionString"] = this.connectionString,
        ["containerName"] = Container,
    };

    [Fact]
    public async Task Azurite_UploadDownloadRoundTrip_ByteIdentical()
    {
        var src = Path.Combine(this.workDir, "up.txt");
        await File.WriteAllTextAsync(src, "azure round trip~ 🫐");

        var up = this.BaseProps("upload");
        up["blobName"] = "folder/up.txt";
        up["localPath"] = src;
        up["createContainer"] = true;
        (await this.module.ExecuteAsync(CloudModuleTestContext.Context(this.services, up))).Success.Should().BeTrue();

        var dst = Path.Combine(this.workDir, "down.txt");
        var down = this.BaseProps("download");
        down["blobName"] = "folder/up.txt";
        down["localPath"] = dst;
        (await this.module.ExecuteAsync(CloudModuleTestContext.Context(this.services, down))).Success.Should().BeTrue();

        (await File.ReadAllTextAsync(dst)).Should().Be("azure round trip~ 🫐");
    }

    [Fact]
    public async Task Azurite_List_WithPrefix_ReturnsSubset()
    {
        foreach (var name in new[] { "x/1.txt", "x/2.txt", "y/3.txt" })
        {
            var f = Path.Combine(this.workDir, Guid.NewGuid().ToString("N"));
            await File.WriteAllTextAsync(f, "x");
            var p = this.BaseProps("upload");
            p["blobName"] = name;
            p["localPath"] = f;
            p["createContainer"] = true;
            await this.module.ExecuteAsync(CloudModuleTestContext.Context(this.services, p));
        }

        var list = this.BaseProps("list");
        list["prefix"] = "x/";
        var result = await this.module.ExecuteAsync(CloudModuleTestContext.Context(this.services, list));

        result.Success.Should().BeTrue();
        ((int)result.Outputs["blobCount"]!).Should().Be(2);
    }

    [Fact]
    public async Task Azurite_Delete_ThenExists_False()
    {
        var f = Path.Combine(this.workDir, "d.txt");
        await File.WriteAllTextAsync(f, "bye");
        var up = this.BaseProps("upload");
        up["blobName"] = "d.txt";
        up["localPath"] = f;
        up["createContainer"] = true;
        await this.module.ExecuteAsync(CloudModuleTestContext.Context(this.services, up));

        var del = this.BaseProps("delete");
        del["blobName"] = "d.txt";
        (await this.module.ExecuteAsync(CloudModuleTestContext.Context(this.services, del))).Success.Should().BeTrue();

        var exists = this.BaseProps("exists");
        exists["blobName"] = "d.txt";
        (await this.module.ExecuteAsync(CloudModuleTestContext.Context(this.services, exists)))
            .Outputs["exists"].Should().Be(false);
    }

    [Fact]
    public async Task Azurite_InvalidConnectionString_FriendlyFail()
    {
        var props = new Dictionary<string, object?>
        {
            ["operation"] = "list",
            ["containerName"] = Container,
            ["connectionString"] = "DefaultEndpointsProtocol=https;AccountName=bogus;AccountKey=Zm9v;EndpointSuffix=core.windows.net",
        };

        var result = await this.module.ExecuteAsync(CloudModuleTestContext.Context(this.services, props));

        result.Success.Should().BeFalse();
    }
}
