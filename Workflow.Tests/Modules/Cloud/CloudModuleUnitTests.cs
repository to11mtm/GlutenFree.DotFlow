// <copyright file="CloudModuleUnitTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Cloud;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Cloud;
using Workflow.Modules.Cloud.Builtin;
using Workflow.Modules.Validation;
using Xunit;

/// <summary>
/// ☁️ Phase 2.5.b.1 — metadata / validation / DI-guard tests for the cloud modules
/// (no live network calls; the MinIO/Azurite matrix lives in Workflow.Tests.Integration)~ ✨.
/// </summary>
public sealed class CloudModuleUnitTests
{
    private readonly S3Module s3 = new();
    private readonly AzureBlobModule azure = new();

    private static ModuleExecutionContext Context(Dictionary<string, object?> props, IServiceProvider services)
        => new()
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = props,
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = services,
            ExecutionId = Guid.NewGuid(),
            NodeId = "cloud-node-1",
        };

    private static ServiceProvider FullServices()
    {
        var sc = new ServiceCollection();
        sc.AddWorkflowModules();      // path validator (for localPath checks)
        sc.AddCloudStorageModules();  // registry + factory
        return sc.BuildServiceProvider();
    }

    [Fact]
    public void S3Module_Metadata_IsValid()
    {
        this.s3.ModuleId.Should().Be("builtin.cloud.s3");
        this.s3.Category.Should().Be("Cloud Storage");
        new ModuleValidator().Validate(this.s3).IsValid.Should().BeTrue();
    }

    [Fact]
    public void AzureBlobModule_Metadata_IsValid()
    {
        this.azure.ModuleId.Should().Be("builtin.cloud.azureblob");
        new ModuleValidator().Validate(this.azure).IsValid.Should().BeTrue();
    }

    [Fact]
    public void S3_Validate_Upload_MissingLocalPath_Fails()
    {
        var result = this.s3.ValidateConfiguration(new Dictionary<string, object?>
        {
            ["operation"] = "upload",
            ["bucket"] = "b",
            ["key"] = "k",
        });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Azure_Validate_Upload_MissingLocalPath_Fails()
    {
        var result = this.azure.ValidateConfiguration(new Dictionary<string, object?>
        {
            ["operation"] = "upload",
            ["containerName"] = "c",
            ["blobName"] = "b",
        });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void S3_Validate_UnknownOperation_Fails()
    {
        this.s3.ValidateConfiguration(new Dictionary<string, object?>
        {
            ["operation"] = "teleport",
            ["bucket"] = "b",
        }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Azure_Validate_UnknownOperation_Fails()
    {
        this.azure.ValidateConfiguration(new Dictionary<string, object?>
        {
            ["operation"] = "teleport",
            ["containerName"] = "c",
        }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void S3_Validate_List_KeyOptional()
    {
        this.s3.ValidateConfiguration(new Dictionary<string, object?>
        {
            ["operation"] = "list",
            ["bucket"] = "b",
        }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Azure_Validate_List_BlobNameOptional()
    {
        this.azure.ValidateConfiguration(new Dictionary<string, object?>
        {
            ["operation"] = "list",
            ["containerName"] = "c",
        }).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task S3_Execute_MissingFactory_Fails()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        var result = await this.s3.ExecuteAsync(Context(new()
        {
            ["operation"] = "list",
            ["bucket"] = "b",
        }, services));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("IStorageClientFactory");
    }

    [Fact]
    public async Task Azure_Execute_MissingFactory_Fails()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        var result = await this.azure.ExecuteAsync(Context(new()
        {
            ["operation"] = "list",
            ["containerName"] = "c",
        }, services));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task S3_Execute_UnknownConnectionId_Fails()
    {
        using var services = FullServices();

        var result = await this.s3.ExecuteAsync(Context(new()
        {
            ["operation"] = "list",
            ["bucket"] = "b",
            ["storageConnectionId"] = "ghost",
        }, services));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Azure_Execute_MissingCredentials_Fails()
    {
        using var services = FullServices();

        var result = await this.azure.ExecuteAsync(Context(new()
        {
            ["operation"] = "list",
            ["containerName"] = "c",
        }, services));

        result.Success.Should().BeFalse("no connectionId or connectionString supplied~");
    }
}
