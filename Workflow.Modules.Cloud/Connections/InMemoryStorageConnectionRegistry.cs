// <copyright file="InMemoryStorageConnectionRegistry.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Cloud.Connections;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.Options;
using Workflow.Modules.Cloud.Abstractions;
using Workflow.Modules.Cloud.Configuration;

/// <summary>
/// 📇 Config-bound <see cref="IStorageConnectionRegistry"/> — hydrates from
/// <see cref="CloudStorageOptions"/> with case-insensitive lookup~ ☁️✨.
/// </summary>
public sealed class InMemoryStorageConnectionRegistry : IStorageConnectionRegistry
{
    private readonly Dictionary<string, StorageConnectionDescriptor> byId;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryStorageConnectionRegistry"/> class.
    /// </summary>
    /// <param name="options">The cloud-storage options.</param>
    public InMemoryStorageConnectionRegistry(IOptions<CloudStorageOptions> options)
    {
        this.byId = new Dictionary<string, StorageConnectionDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in options.Value.Connections)
        {
            if (!string.IsNullOrWhiteSpace(c.Id))
            {
                this.byId[c.Id] = c;
            }
        }
    }

    /// <inheritdoc />
    public bool TryGet(string id, [NotNullWhen(true)] out StorageConnectionDescriptor descriptor)
    {
        if (!string.IsNullOrWhiteSpace(id) && this.byId.TryGetValue(id, out var found) && found.Enabled)
        {
            descriptor = found;
            return true;
        }

        descriptor = null!;
        return false;
    }

    /// <inheritdoc />
    public IReadOnlyList<StorageConnectionDescriptor> List() => this.byId.Values.ToList();
}
