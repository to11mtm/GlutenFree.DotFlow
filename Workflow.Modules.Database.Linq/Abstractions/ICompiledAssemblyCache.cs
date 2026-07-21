// <copyright file="ICompiledAssemblyCache.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq.Abstractions;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Workflow.Modules.Database.Abstractions;

/// <summary>
/// 📦 Hash-keyed cache for compiled linq assemblies, backed by <c>IBlobStore</c> + an in-memory LRU (2.4.b.2)~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: Blobs live under <c>compiled-modules/{definitionId}/{nodeId}/{hash}.dll</c> (§8.3, D15).
/// Bytes are HMAC-signed on write and verified on read (tamper → cache miss), so a swapped blob is
/// rejected before it ever reaches the 2.4.b.3 ALC loader~ 🔒.
/// </remarks>
public interface ICompiledAssemblyCache
{
    /// <summary>
    /// Computes the stable blob key for a compile input. Identical inputs → identical key (no recompile);
    /// any change to code, schema version, or selected tables → a new key (auto-invalidation)~ 🔑.
    /// </summary>
    /// <param name="definitionId">Owning workflow definition id.</param>
    /// <param name="nodeId">The linq node id.</param>
    /// <param name="userCode">The user's method body.</param>
    /// <param name="schemaVersion">The codegen schema version (see <c>LinqCodegen.SchemaVersion</c>).</param>
    /// <param name="selectedTables">The node's selected tables (order-independent).</param>
    /// <returns>The blob key.</returns>
    string ComputeKey(
        string definitionId,
        string nodeId,
        string userCode,
        string schemaVersion,
        IReadOnlyList<WorkflowTableMetadata> selectedTables);

    /// <summary>
    /// Returns the verified assembly bytes for a key, or <c>null</c> on miss / tamper / not-found~ ⬇️.
    /// </summary>
    /// <param name="key">The blob key from <see cref="ComputeKey"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The assembly bytes, or <c>null</c>.</returns>
    ValueTask<byte[]?> TryGetAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Signs + stores assembly bytes at the key (blob + LRU)~ ⬆️.
    /// </summary>
    /// <param name="key">The blob key from <see cref="ComputeKey"/>.</param>
    /// <param name="assemblyBytes">The raw emitted assembly bytes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the async operation.</returns>
    ValueTask StoreAsync(string key, byte[] assemblyBytes, CancellationToken ct = default);

    /// <summary>
    /// Deletes all known cached blobs for a definition + drops their LRU entries~ 🗑️.
    /// </summary>
    /// <param name="definitionId">The definition whose compiled blobs should be evicted.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of blobs deleted.</returns>
    ValueTask<int> EvictDefinitionAsync(string definitionId, CancellationToken ct = default);
}

