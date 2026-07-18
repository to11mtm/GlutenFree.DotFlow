// <copyright file="StorageInfrastructureTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Cloud;

using FluentAssertions;
using Microsoft.Extensions.Options;
using Workflow.Modules.Cloud.Abstractions;
using Workflow.Modules.Cloud.Configuration;
using Workflow.Modules.Cloud.Connections;
using Xunit;

/// <summary>
/// ☁️ Phase 2.5.b.0 — tests for the storage connection registry + client factory (Docker-free)~ ✨.
/// </summary>
public sealed class StorageInfrastructureTests
{
    private static InMemoryStorageConnectionRegistry BuildRegistry(params StorageConnectionDescriptor[] connections)
    {
        var options = new CloudStorageOptions();
        options.Connections.AddRange(connections);
        return new InMemoryStorageConnectionRegistry(Options.Create(options));
    }

    [Fact]
    public void Registry_ConfigBoundEntry_LookupById()
    {
        var registry = BuildRegistry(new StorageConnectionDescriptor("my-s3", "s3", Region: "us-west-2"));

        registry.TryGet("my-s3", out var descriptor).Should().BeTrue();
        descriptor.Region.Should().Be("us-west-2");
    }

    [Fact]
    public void Registry_LookupCaseInsensitive()
    {
        var registry = BuildRegistry(new StorageConnectionDescriptor("My-S3", "s3"));

        registry.TryGet("my-s3", out _).Should().BeTrue();
    }

    [Fact]
    public void Registry_UnknownId_ReturnsFalse()
    {
        var registry = BuildRegistry();

        registry.TryGet("nope", out _).Should().BeFalse();
    }

    [Fact]
    public void Registry_DisabledConnection_ExcludedFromResolution()
    {
        var registry = BuildRegistry(new StorageConnectionDescriptor("off", "s3", Enabled: false));

        registry.TryGet("off", out _).Should().BeFalse();
        registry.List().Should().HaveCount(1, "disabled connections still appear in List()~");
    }

    [Fact]
    public void Factory_S3_ExplicitKeys_BuildsClientWithServiceUrl()
    {
        var factory = new DefaultStorageClientFactory(BuildRegistry());

        using var client = factory.CreateS3Client(null, "AKIA", "secret", serviceUrl: "http://localhost:9000");

        client.Config.ServiceURL.Should().StartWith("http://localhost:9000");
    }

    [Fact]
    public void Factory_S3_NoCredentials_FallsBackToDefaultChain()
    {
        var factory = new DefaultStorageClientFactory(BuildRegistry());

        // Should not throw — the SDK resolves credentials lazily from the default chain~ 🔑
        using var client = factory.CreateS3Client(null, region: "us-east-1");
        client.Should().NotBeNull();
    }

    [Fact]
    public void Factory_S3_NamedConnectionWrongKind_Throws()
    {
        var factory = new DefaultStorageClientFactory(
            BuildRegistry(new StorageConnectionDescriptor("blob1", "azureBlob", ConnectionString: "UseDevelopmentStorage=true")));

        var act = () => factory.CreateS3Client("blob1");

        act.Should().Throw<UnknownStorageKindException>();
    }

    [Fact]
    public void Factory_S3_UnknownConnectionId_Throws()
    {
        var factory = new DefaultStorageClientFactory(BuildRegistry());

        var act = () => factory.CreateS3Client("ghost");

        act.Should().Throw<StorageConnectionNotFoundException>();
    }

    [Fact]
    public void Factory_AzureBlob_ConnectionString_BuildsClient()
    {
        var factory = new DefaultStorageClientFactory(BuildRegistry());

        var client = factory.CreateBlobServiceClient(null, "UseDevelopmentStorage=true");

        client.Should().NotBeNull();
    }

    [Fact]
    public void Descriptor_ToString_RedactsSecrets()
    {
        var descriptor = new StorageConnectionDescriptor(
            "s", "s3", AccessKey: "AKIAEXAMPLE", SecretKey: "topsecret", ConnectionString: "secretconn");

        var text = descriptor.ToString();

        text.Should().NotContain("AKIAEXAMPLE").And.NotContain("topsecret").And.NotContain("secretconn");
        text.Should().Contain("***REDACTED***");
    }
}
