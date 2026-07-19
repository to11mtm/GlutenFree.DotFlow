// <copyright file="ModuleHostAdapters.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Modules;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Workflow.Api.Observability;
using Workflow.Modules.Loading;
using Workflow.Modules.Packaging;
using Workflow.Modules.State;
using Workflow.Persistence.Abstractions;

/// <summary>
/// 🗄️ Phase 2.8 — Adapts <see cref="IBlobStore"/> to the module-layer <see cref="IModulePackageArchive"/>
/// seam so uploaded <c>.wfmod</c> bytes can be archived for re-provisioning (Q1)~ ✨.
/// </summary>
public sealed class BlobStoreModulePackageArchive : IModulePackageArchive
{
    private readonly IBlobStore blobStore;

    /// <summary>Initializes a new instance of the <see cref="BlobStoreModulePackageArchive"/> class~ 🗄️.</summary>
    /// <param name="blobStore">The blob store.</param>
    public BlobStoreModulePackageArchive(IBlobStore blobStore)
    {
        this.blobStore = blobStore ?? throw new ArgumentNullException(nameof(blobStore));
    }

    /// <inheritdoc/>
    public async Task ArchiveAsync(string moduleId, Version version, byte[] packageBytes, CancellationToken ct = default)
    {
        var key = $"modules/packages/{moduleId}/{version}.wfmod";
        using var stream = new MemoryStream(packageBytes, writable: false);
        await this.blobStore.PutAsync(key, stream, "application/zip", ct).ConfigureAwait(false);
    }
}

/// <summary>
/// 🗄️ Phase 2.8 — Adapts <see cref="IBlobStore"/> to the module-layer <see cref="IModuleStatePersistence"/>
/// seam so module enabled/installed state can persist via a configured provider (Q2 repository mode)~ ✨.
/// </summary>
public sealed class BlobStoreModuleStatePersistence : IModuleStatePersistence
{
    private const string Key = "modules/state.json";
    private readonly IBlobStore blobStore;

    /// <summary>Initializes a new instance of the <see cref="BlobStoreModuleStatePersistence"/> class~ 🗄️.</summary>
    /// <param name="blobStore">The blob store.</param>
    public BlobStoreModuleStatePersistence(IBlobStore blobStore)
    {
        this.blobStore = blobStore ?? throw new ArgumentNullException(nameof(blobStore));
    }

    /// <inheritdoc/>
    public async Task<string?> ReadAsync(CancellationToken ct = default)
    {
        var stream = await this.blobStore.GetAsync(Key, ct).ConfigureAwait(false);
        if (stream is null)
        {
            return null;
        }

        using (stream)
        using (var reader = new StreamReader(stream))
        {
            return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task WriteAsync(string json, CancellationToken ct = default)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        using var stream = new MemoryStream(bytes, writable: false);
        await this.blobStore.PutAsync(Key, stream, "application/json", ct).ConfigureAwait(false);
    }
}

/// <summary>
/// 🛟 Phase 2.8.3 — Adapts <see cref="IWorkflowMetrics"/> to the module-layer
/// <see cref="IActiveExecutionTracker"/> unload-safety gate~ ✨.
/// </summary>
public sealed class MetricsActiveExecutionTracker : IActiveExecutionTracker
{
    private readonly IWorkflowMetrics metrics;

    /// <summary>Initializes a new instance of the <see cref="MetricsActiveExecutionTracker"/> class~ 🛟.</summary>
    /// <param name="metrics">The workflow metrics seam.</param>
    public MetricsActiveExecutionTracker(IWorkflowMetrics metrics)
    {
        this.metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    /// <inheritdoc/>
    public bool HasActiveExecutions => this.metrics.Snapshot().Active > 0;
}

/// <summary>
/// 🗄️ Phase 2.8 — A no-op archive used when no blob store is available (archival simply skipped)~ ✨.
/// </summary>
public sealed class NoOpModulePackageArchive : IModulePackageArchive
{
    /// <inheritdoc/>
    public Task ArchiveAsync(string moduleId, Version version, byte[] packageBytes, CancellationToken ct = default)
        => Task.CompletedTask;
}

/// <summary>
/// 🗄️ Phase 3.1.5 — Adapts <see cref="IBlobStore"/> to the script library persistence seam~ ✨.
/// </summary>
public sealed class BlobScriptLibraryPersistence : Workflow.Scripting.Libraries.IScriptLibraryPersistence
{
    private const string Key = "scripts/libraries.json";
    private readonly IBlobStore blobStore;

    /// <summary>Initializes a new instance of the <see cref="BlobScriptLibraryPersistence"/> class~ 🗄️.</summary>
    /// <param name="blobStore">The blob store.</param>
    public BlobScriptLibraryPersistence(IBlobStore blobStore)
    {
        this.blobStore = blobStore ?? throw new ArgumentNullException(nameof(blobStore));
    }

    /// <inheritdoc/>
    public async Task<string?> ReadAsync(CancellationToken ct = default)
    {
        var stream = await this.blobStore.GetAsync(Key, ct).ConfigureAwait(false);
        if (stream is null)
        {
            return null;
        }

        using (stream)
        using (var reader = new StreamReader(stream))
        {
            return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task WriteAsync(string json, CancellationToken ct = default)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        using var stream = new MemoryStream(bytes, writable: false);
        await this.blobStore.PutAsync(Key, stream, "application/json", ct).ConfigureAwait(false);
    }
}

/// <summary>
/// 🗄️ Phase 2.8 — A no-op state persistence used when no blob store is available (the state store
/// factory then falls back to the file store)~ ✨.
/// </summary>
public sealed class NoOpModuleStatePersistence : IModuleStatePersistence
{
    /// <inheritdoc/>
    public Task<string?> ReadAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);

    /// <inheritdoc/>
    public Task WriteAsync(string json, CancellationToken ct = default) => Task.CompletedTask;
}
