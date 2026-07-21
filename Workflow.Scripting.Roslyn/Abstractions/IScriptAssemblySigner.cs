// <copyright file="IScriptAssemblySigner.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Roslyn.Abstractions;

/// <summary>
/// 🔑 Provides the HMAC key used to sign/verify cached script assemblies~ ✨.
/// </summary>
/// <remarks>
/// Default is an ephemeral per-process key (safe; the on-disk cache is recomputed once after a
/// restart). Hosts may register a Data-Protection-backed stable key for cross-restart reuse.
/// </remarks>
public interface IScriptHmacKeyProvider
{
    /// <summary>Gets the HMAC key bytes~ 🔑.</summary>
    /// <returns>The key.</returns>
    byte[] GetKey();
}

/// <summary>
/// 🔏 Signs/verifies compiled script assembly bytes so tampered cache entries never reach the loader~ ✨.
/// </summary>
public interface IScriptAssemblySigner
{
    /// <summary>
    /// Prepends an HMAC tag to the assembly bytes for storage~ 🔏.
    /// </summary>
    /// <param name="assemblyBytes">The raw assembly bytes.</param>
    /// <returns>Tagged bytes (tag + payload).</returns>
    byte[] Sign(byte[] assemblyBytes);

    /// <summary>
    /// Verifies + strips the HMAC tag~ 🔍.
    /// </summary>
    /// <param name="taggedBytes">The stored (tagged) bytes.</param>
    /// <param name="assemblyBytes">The verified payload when successful.</param>
    /// <returns><c>true</c> when the tag verifies; otherwise <c>false</c>.</returns>
    bool TryVerify(byte[] taggedBytes, out byte[] assemblyBytes);
}
