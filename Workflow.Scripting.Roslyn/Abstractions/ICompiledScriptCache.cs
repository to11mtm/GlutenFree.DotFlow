// <copyright file="ICompiledScriptCache.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Roslyn.Abstractions;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 📦 HMAC-verified, LRU-fronted cache of compiled script assemblies over an <c>IBlobStore</c>~ ✨.
/// </summary>
public interface ICompiledScriptCache
{
    /// <summary>
    /// Stores signed assembly bytes under the given key~ ⬆️.
    /// </summary>
    /// <param name="key">The blob key.</param>
    /// <param name="assemblyBytes">The raw (unsigned) assembly bytes.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The key.</returns>
    Task<string> StoreAsync(string key, byte[] assemblyBytes, CancellationToken ct = default);

    /// <summary>
    /// Retrieves + verifies assembly bytes; tampered/missing → <c>null</c>~ ⬇️.
    /// </summary>
    /// <param name="key">The blob key.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The verified bytes, or <c>null</c>.</returns>
    Task<byte[]?> TryGetAsync(string key, CancellationToken ct = default);
}
