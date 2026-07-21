// <copyright file="CloudStorageOptions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Cloud.Configuration;

using System.Collections.Generic;
using Workflow.Modules.Cloud.Abstractions;

/// <summary>
/// ⚙️ Configuration options for the cloud-storage module family~ ☁️✨.
/// </summary>
/// <remarks>
/// Bind from <see cref="SectionName"/> (e.g. <c>Workflow:CloudStorage</c>). Each entry becomes a
/// <see cref="StorageConnectionDescriptor"/> in the registry~ 🌸.
/// </remarks>
public sealed class CloudStorageOptions
{
    /// <summary>
    /// Configuration section name for binding~ 🏷️.
    /// </summary>
    public const string SectionName = "Workflow:CloudStorage";

    /// <summary>
    /// Gets the named storage connections~ 📇.
    /// </summary>
    public List<StorageConnectionDescriptor> Connections { get; } = new();
}
