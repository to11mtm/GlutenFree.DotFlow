// <copyright file="MinioS3ModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Integration.Cloud;

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Minio;
using Workflow.Modules.Cloud.Builtin;
using Xunit;

/// <summary>
/// 🪣 Phase 2.5.b.1 — MinIO-backed integration tests for <see cref="S3Module"/> (Docker-gated)~ ☁️✨.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MinioS3ModuleTests : IAsyncLifetime
{
    private const string Bucket = "wf-s3-module-test";
    private const string Access = "minioadmin";
    private const string Secret = "minioadmin";

    private readonly MinioContainer container = new MinioBuilder()
        .WithImage("minio/minio:latest")
        .WithUsername(Access)
        .WithPassword(Secret)
        .Build();

    private readonly S3Module module = new();
    private ServiceProvider services = null!;
    private string endpoint = null!;
    private string workDir = null!;

    public async Task InitializeAsync()
    {
        await this.container.StartAsync();
        this.endpoint = $"http://{this.container.Hostname}:{this.container.GetMappedPublicPort(9000)}";
        this.services = CloudModuleTestContext.BuildServices();
        this.workDir = Path.Combine(Path.GetTempPath(), "wf-s3-it-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.workDir);

        var config = new AmazonS3Config { ServiceURL = this.endpoint, ForcePathStyle = true };
        using var client = new AmazonS3Client(Access, Secret, config);
        await client.PutBucketAsync(new PutBucketRequest { BucketName = Bucket });
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
        ["accessKey"] = Access,
        ["secretKey"] = Secret,
        ["serviceUrl"] = this.endpoint,
        ["bucket"] = Bucket,
    };

    [Fact]
    public async Task Minio_UploadDownloadRoundTrip_ByteIdentical()
    {
        var src = Path.Combine(this.workDir, "up.txt");
        await File.WriteAllTextAsync(src, "s3 round trip~ 🪣");

        var upProps = this.BaseProps("upload");
        upProps["key"] = "folder/up.txt";
        upProps["localPath"] = src;
        var up = await this.module.ExecuteAsync(CloudModuleTestContext.Context(this.services, upProps));
        up.Success.Should().BeTrue();

        var dst = Path.Combine(this.workDir, "down.txt");
        var downProps = this.BaseProps("download");
        downProps["key"] = "folder/up.txt";
        downProps["localPath"] = dst;
        var down = await this.module.ExecuteAsync(CloudModuleTestContext.Context(this.services, downProps));
        down.Success.Should().BeTrue();

        (await File.ReadAllTextAsync(dst)).Should().Be("s3 round trip~ 🪣");
    }

    [Fact]
    public async Task Minio_List_WithPrefix_ReturnsSubset()
    {
        foreach (var name in new[] { "a/1.txt", "a/2.txt", "b/3.txt" })
        {
            var f = Path.Combine(this.workDir, System.Guid.NewGuid().ToString("N") + ".txt");
            await File.WriteAllTextAsync(f, "x");
            var p = this.BaseProps("upload");
            p["key"] = name;
            p["localPath"] = f;
            await this.module.ExecuteAsync(CloudModuleTestContext.Context(this.services, p));
        }

        var listProps = this.BaseProps("list");
        listProps["prefix"] = "a/";
        var list = await this.module.ExecuteAsync(CloudModuleTestContext.Context(this.services, listProps));

        list.Success.Should().BeTrue();
        ((int)list.Outputs["objectCount"]!).Should().Be(2);
    }

    [Fact]
    public async Task Minio_Delete_ThenExists_False()
    {
        var f = Path.Combine(this.workDir, "del.txt");
        await File.WriteAllTextAsync(f, "bye");
        var up = this.BaseProps("upload");
        up["key"] = "del.txt";
        up["localPath"] = f;
        await this.module.ExecuteAsync(CloudModuleTestContext.Context(this.services, up));

        var del = this.BaseProps("delete");
        del["key"] = "del.txt";
        (await this.module.ExecuteAsync(CloudModuleTestContext.Context(this.services, del))).Success.Should().BeTrue();

        var exists = this.BaseProps("exists");
        exists["key"] = "del.txt";
        var result = await this.module.ExecuteAsync(CloudModuleTestContext.Context(this.services, exists));
        result.Outputs["exists"].Should().Be(false);
    }

    [Fact]
    public async Task Minio_WrongCredentials_FailsWithoutLeakingSecret()
    {
        var f = Path.Combine(this.workDir, "x.txt");
        await File.WriteAllTextAsync(f, "x");
        var p = this.BaseProps("upload");
        p["secretKey"] = "totally-wrong-secret";
        p["key"] = "x.txt";
        p["localPath"] = f;

        var result = await this.module.ExecuteAsync(CloudModuleTestContext.Context(this.services, p));

        result.Success.Should().BeFalse();
        (result.ErrorMessage ?? string.Empty).Should().NotContain("totally-wrong-secret");
    }
}
