// <copyright file="IStorageConnectionRegistry.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Cloud.Abstractions;

using System.Collections.Generic;

/// <summary>
/// 📇 A named cloud-storage connection descriptor (credentials hidden from workflow definitions)~ ☁️.
/// </summary>
/// <param name="Id">The unique connection id referenced by modules.</param>
/// <param name="Kind">The storage kind — <c>"s3"</c> or <c>"azureBlob"</c>.</param>
/// <param name="Enabled">Whether the connection is active.</param>
/// <param name="AccessKey">S3 access key (nullable — falls back to the default chain).</param>
/// <param name="SecretKey">S3 secret key (secret).</param>
/// <param name="Region">S3 region.</param>
/// <param name="ServiceUrl">S3 custom endpoint (MinIO / on-prem).</param>
/// <param name="ConnectionString">Azure Blob connection string (secret).</param>
public record StorageConnectionDescriptor(
    string Id,
    string Kind,
    bool Enabled = true,
    string? AccessKey = null,
    string? SecretKey = null,
    string? Region = null,
    string? ServiceUrl = null,
    string? ConnectionString = null)
{
    /// <summary>
    /// Returns a redacted string that never leaks secret material~ 🛡️.
    /// </summary>
    /// <returns>A safe-to-log representation.</returns>
    public override string ToString()
        => $"StorageConnection(Id={this.Id}, Kind={this.Kind}, Enabled={this.Enabled}, "
            + $"Region={this.Region ?? "-"}, ServiceUrl={this.ServiceUrl ?? "-"}, "
            + $"AccessKey={Redact(this.AccessKey)}, SecretKey={Redact(this.SecretKey)}, "
            + $"ConnectionString={Redact(this.ConnectionString)})";

    private static string Redact(string? value)
        => string.IsNullOrEmpty(value) ? "-" : "***REDACTED***";
}

/// <summary>
/// 📇 Registry of named storage connections, hydrated from configuration (D5/D14)~ ☁️✨.
/// </summary>
public interface IStorageConnectionRegistry
{
    /// <summary>
    /// Attempts to resolve an enabled connection by id~ 🔍.
    /// </summary>
    /// <param name="id">The connection id.</param>
    /// <param name="descriptor">The resolved descriptor when found and enabled.</param>
    /// <returns><c>true</c> when found and enabled; otherwise <c>false</c>.</returns>
    bool TryGet(string id, out StorageConnectionDescriptor descriptor);

    /// <summary>
    /// Lists all registered connections (including disabled)~ 📋.
    /// </summary>
    /// <returns>All descriptors.</returns>
    IReadOnlyList<StorageConnectionDescriptor> List();
}
