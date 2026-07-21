// <copyright file="CloudModuleException.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Cloud.Abstractions;

using System;

/// <summary>
/// 🚨 Base exception for cloud-storage module failures~ ☁️.
/// </summary>
public class CloudModuleException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CloudModuleException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="inner">The optional inner exception.</param>
    public CloudModuleException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// 📇 Thrown when a named storage connection cannot be found~ 🔍.
/// </summary>
public sealed class StorageConnectionNotFoundException : CloudModuleException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StorageConnectionNotFoundException"/> class.
    /// </summary>
    /// <param name="connectionId">The connection id that was not found.</param>
    public StorageConnectionNotFoundException(string connectionId)
        : base($"☁️ Storage connection '{connectionId}' not found or disabled~ 🔍")
    {
        this.ConnectionId = connectionId;
    }

    /// <summary>
    /// Gets the connection id that was not found~ 🆔.
    /// </summary>
    public string ConnectionId { get; }
}

/// <summary>
/// 🧭 Thrown when a storage connection declares an unknown <c>Kind</c>~ ❓.
/// </summary>
public sealed class UnknownStorageKindException : CloudModuleException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnknownStorageKindException"/> class.
    /// </summary>
    /// <param name="kind">The unknown storage kind.</param>
    public UnknownStorageKindException(string kind)
        : base($"☁️ Unknown storage kind '{kind}' (expected s3 or azureBlob)~ ❓")
    {
        this.Kind = kind;
    }

    /// <summary>
    /// Gets the unknown storage kind~ 🏷️.
    /// </summary>
    public string Kind { get; }
}
