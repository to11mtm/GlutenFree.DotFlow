// <copyright file="FileCloudE2ETests.cs" company="GlutenFree">
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
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.File;
using Workflow.Modules.Cloud;
using Workflow.Modules.Cloud.Builtin;
using Xunit;

/// <summary>
/// 📖 Phase 2.5.b.2 — end-to-end demo chaining the file + compression + cloud families~ ☁️✨.
/// </summary>
[Trait("Category", "Integration")]
public sealed class FileCloudE2ETests : IAsyncLifetime
{
    private const string Bucket = "wf-e2e-bucket";
    private const string Access = "minioadmin";
    private const string Secret = "minioadmin";

    private readonly MinioContainer container = new MinioBuilder()
        .WithImage("minio/minio:latest")
        .WithUsername(Access)
        .WithPassword(Secret)
        .Build();

    private ServiceProvider services = null!;
    private string endpoint = null!;
    private string workDir = null!;

    public async Task InitializeAsync()
    {
        await this.container.StartAsync();
        this.endpoint = $"http://{this.container.Hostname}:{this.container.GetMappedPublicPort(9000)}";
        this.services = CloudModuleTestContext.BuildServices();
        this.workDir = Path.Combine(Path.GetTempPath(), "wf-e2e-" + System.Guid.NewGuid().ToString("N"));
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

    private ModuleExecutionContext Ctx(Dictionary<string, object?> props, Dictionary<string, object?>? inputs = null)
        => new()
        {
            Inputs = inputs ?? new Dictionary<string, object?>(),
            Properties = props,
            Variables = new Dictionary<string, object?>(),
            Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            Services = this.services,
            ExecutionId = System.Guid.NewGuid(),
            NodeId = "e2e",
        };

    [Fact]
    public async Task E2E_CsvToJsonToZipToS3AndBack_ByteIdentical()
    {
        // 1) Read a CSV → 2) write JSON → 3) zip → 4) S3 upload → 5) S3 download → 6) decompress → 7) verify
        var csvPath = Path.Combine(this.workDir, "in.csv");
        await File.WriteAllTextAsync(csvPath, "name,age\nAda,36\nGrace,45\n");

        var csvRead = await new CsvReadModule().ExecuteAsync(this.Ctx(new() { ["path"] = csvPath }));
        csvRead.Success.Should().BeTrue();

        var jsonPath = Path.Combine(this.workDir, "out.json");
        var jsonWrite = await new JsonWriteModule().ExecuteAsync(this.Ctx(
            new() { ["path"] = jsonPath },
            new() { ["data"] = csvRead.Outputs["rows"] }));
        jsonWrite.Success.Should().BeTrue();

        var zipPath = Path.Combine(this.workDir, "bundle.zip");
        var compress = await new CompressModule().ExecuteAsync(this.Ctx(new()
        {
            ["sourcePath"] = jsonPath,
            ["outputPath"] = zipPath,
            ["format"] = "zip",
        }));
        compress.Success.Should().BeTrue();

        var s3 = new S3Module();
        var up = await s3.ExecuteAsync(this.Ctx(new()
        {
            ["operation"] = "upload",
            ["accessKey"] = Access,
            ["secretKey"] = Secret,
            ["serviceUrl"] = this.endpoint,
            ["bucket"] = Bucket,
            ["key"] = "bundle.zip",
            ["localPath"] = zipPath,
        }));
        up.Success.Should().BeTrue();

        var downloadedZip = Path.Combine(this.workDir, "downloaded.zip");
        var down = await s3.ExecuteAsync(this.Ctx(new()
        {
            ["operation"] = "download",
            ["accessKey"] = Access,
            ["secretKey"] = Secret,
            ["serviceUrl"] = this.endpoint,
            ["bucket"] = Bucket,
            ["key"] = "bundle.zip",
            ["localPath"] = downloadedZip,
        }));
        down.Success.Should().BeTrue();

        var extractDir = Path.Combine(this.workDir, "extracted");
        var decompress = await new DecompressModule().ExecuteAsync(this.Ctx(new()
        {
            ["archivePath"] = downloadedZip,
            ["outputDirectory"] = extractDir,
        }));
        decompress.Success.Should().BeTrue();

        var extractedJson = Path.Combine(extractDir, "out.json");
        File.Exists(extractedJson).Should().BeTrue();
        (await File.ReadAllTextAsync(extractedJson)).Should().Be(await File.ReadAllTextAsync(jsonPath));
    }

    [Fact]
    public async Task E2E_SandboxedRoots_WorkflowCannotEscape()
    {
        // Build a sandboxed service provider whose file root is workDir~ 🛡️
        var sc = new ServiceCollection();
        sc.AddWorkflowModules();
        sc.AddCloudStorageModules();
        sc.Configure<Workflow.Modules.Builtin.File.FileSystemModuleOptions>(o =>
            o.AllowedRoots = new[] { this.workDir });
        using var sandboxed = sc.BuildServiceProvider();

        var ctx = new ModuleExecutionContext
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = new Dictionary<string, object?> { ["path"] = Path.Combine(Path.GetTempPath(), "escape.txt"), ["content"] = "nope" },
            Variables = new Dictionary<string, object?>(),
            Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            Services = sandboxed,
            ExecutionId = System.Guid.NewGuid(),
            NodeId = "e2e-sandbox",
        };

        var result = await new FileWriteModule().ExecuteAsync(ctx);

        result.Success.Should().BeFalse("a path outside the sandbox root must be rejected~ 🛡️");
    }
}
