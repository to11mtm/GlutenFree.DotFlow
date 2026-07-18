// <copyright file="IStorageClientFactory.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Cloud.Abstractions;

using Amazon.S3;
using Azure.Storage.Blobs;

/// <summary>
/// 🔌 Builds live cloud-storage SDK clients from named connections or inline credentials~ ☁️✨.
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.5.b.0. The S3 path falls back to the default AWS credential chain when no
/// explicit keys are supplied (Q6). Callers own client disposal~ 🧹.
/// </remarks>
public interface IStorageClientFactory
{
    /// <summary>
    /// Creates an S3 client from a named connection (preferred) or inline credentials~ 🪣.
    /// </summary>
    /// <param name="connectionId">The named connection id, or <c>null</c> for inline/chain.</param>
    /// <param name="accessKey">Inline access key (ignored when <paramref name="connectionId"/> is set).</param>
    /// <param name="secretKey">Inline secret key.</param>
    /// <param name="region">Inline region.</param>
    /// <param name="serviceUrl">Inline custom endpoint (MinIO/on-prem).</param>
    /// <returns>A live <see cref="IAmazonS3"/> client (caller disposes).</returns>
    IAmazonS3 CreateS3Client(
        string? connectionId,
        string? accessKey = null,
        string? secretKey = null,
        string? region = null,
        string? serviceUrl = null);

    /// <summary>
    /// Creates an Azure Blob service client from a named connection or inline connection string~ 🫐.
    /// </summary>
    /// <param name="connectionId">The named connection id, or <c>null</c> for inline.</param>
    /// <param name="connectionString">Inline connection string.</param>
    /// <returns>A live <see cref="BlobServiceClient"/>.</returns>
    BlobServiceClient CreateBlobServiceClient(string? connectionId, string? connectionString = null);
}
