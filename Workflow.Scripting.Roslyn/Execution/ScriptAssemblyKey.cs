// <copyright file="ScriptAssemblyKey.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Roslyn.Execution;

using System;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// 🔑 Computes the deterministic blob-store key for a compiled script assembly~ ✨.
/// </summary>
/// <remarks>
/// Key shape: <c>{prefix}/{definitionId}/{nodeId}/{SHA256(code + schemaVersion + inputsFingerprint)}.dll</c>.
/// The hash makes cache invalidation automatic when any input changes~ 🌸.
/// </remarks>
public static class ScriptAssemblyKey
{
    /// <summary>
    /// Builds the cache key~ 🔑.
    /// </summary>
    /// <param name="namespacePrefix">The blob namespace prefix (e.g. <c>compiled-modules/transform</c>).</param>
    /// <param name="definitionId">The workflow definition id.</param>
    /// <param name="nodeId">The node id.</param>
    /// <param name="userCode">The user script body.</param>
    /// <param name="schemaVersion">The codegen schema version.</param>
    /// <param name="inputsFingerprint">A stable fingerprint of the inputs shape.</param>
    /// <returns>The blob key.</returns>
    public static string Build(
        string namespacePrefix,
        string definitionId,
        string nodeId,
        string userCode,
        int schemaVersion,
        string inputsFingerprint)
    {
        var payload = $"{userCode}\u0001{schemaVersion}\u0001{inputsFingerprint}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return $"{namespacePrefix.TrimEnd('/')}/{definitionId}/{nodeId}/{hash}.dll";
    }
}
