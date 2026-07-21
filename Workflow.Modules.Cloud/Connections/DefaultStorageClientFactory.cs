// <copyright file="DefaultStorageClientFactory.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Cloud.Connections;

using System;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Azure.Storage.Blobs;
using Workflow.Modules.Cloud.Abstractions;

/// <summary>
/// 🔌 Default <see cref="IStorageClientFactory"/> — resolves named connections then builds
/// AWS/Azure SDK clients~ ☁️✨.
/// </summary>
public sealed class DefaultStorageClientFactory : IStorageClientFactory
{
    private readonly IStorageConnectionRegistry registry;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultStorageClientFactory"/> class.
    /// </summary>
    /// <param name="registry">The storage connection registry.</param>
    public DefaultStorageClientFactory(IStorageConnectionRegistry registry)
    {
        this.registry = registry;
    }

    /// <inheritdoc />
    public IAmazonS3 CreateS3Client(
        string? connectionId,
        string? accessKey = null,
        string? secretKey = null,
        string? region = null,
        string? serviceUrl = null)
    {
        if (!string.IsNullOrWhiteSpace(connectionId))
        {
            if (!this.registry.TryGet(connectionId, out var descriptor))
            {
                throw new StorageConnectionNotFoundException(connectionId);
            }

            if (!string.Equals(descriptor.Kind, "s3", StringComparison.OrdinalIgnoreCase))
            {
                throw new UnknownStorageKindException(descriptor.Kind);
            }

            accessKey = descriptor.AccessKey;
            secretKey = descriptor.SecretKey;
            region = descriptor.Region;
            serviceUrl = descriptor.ServiceUrl;
        }

        var config = new AmazonS3Config();
        if (!string.IsNullOrWhiteSpace(serviceUrl))
        {
            config.ServiceURL = serviceUrl;
            config.ForcePathStyle = true; // required for MinIO / on-prem~ 🪣
        }
        else if (!string.IsNullOrWhiteSpace(region))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(region);
        }

        // Q6 — explicit keys when supplied, else the default AWS credential chain~ 🔑
        if (!string.IsNullOrWhiteSpace(accessKey) && !string.IsNullOrWhiteSpace(secretKey))
        {
            return new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), config);
        }

        return new AmazonS3Client(config);
    }

    /// <inheritdoc />
    public BlobServiceClient CreateBlobServiceClient(string? connectionId, string? connectionString = null)
    {
        if (!string.IsNullOrWhiteSpace(connectionId))
        {
            if (!this.registry.TryGet(connectionId, out var descriptor))
            {
                throw new StorageConnectionNotFoundException(connectionId);
            }

            if (!string.Equals(descriptor.Kind, "azureBlob", StringComparison.OrdinalIgnoreCase))
            {
                throw new UnknownStorageKindException(descriptor.Kind);
            }

            connectionString = descriptor.ConnectionString;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new CloudModuleException("☁️ Azure Blob requires a storageConnectionId or connectionString~ 💔");
        }

        return new BlobServiceClient(connectionString);
    }
}
