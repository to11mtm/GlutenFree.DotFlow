// <copyright file="ILinqAssemblySigner.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq.Abstractions;

/// <summary>
/// 🔒 Signs + verifies compiled linq assembly blobs so swapped/tampered blobs are rejected (2.4.b.2)~ ✨.
/// </summary>
public interface ILinqAssemblySigner
{
    /// <summary>Wraps assembly bytes with an integrity tag for storage~ 🔐.</summary>
    /// <param name="assemblyBytes">The raw emitted assembly bytes.</param>
    /// <returns>The signed payload (tag + bytes) to persist.</returns>
    byte[] Sign(byte[] assemblyBytes);

    /// <summary>Verifies + unwraps a stored payload~ 🔓.</summary>
    /// <param name="signedPayload">The stored payload from <see cref="Sign"/>.</param>
    /// <param name="assemblyBytes">The recovered assembly bytes when verification succeeds.</param>
    /// <returns><c>true</c> when the payload is authentic; <c>false</c> when tampered/corrupt.</returns>
    bool TryVerify(byte[] signedPayload, out byte[] assemblyBytes);
}

/// <summary>
/// 🗝️ Supplies the HMAC key for <see cref="ILinqAssemblySigner"/>~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: The Linq project ships an ephemeral (per-process random) default. The host may
/// register a stable Data-Protection-backed key (mirrors the 2.4.a.5 connection-string protector) so
/// the on-disk cache survives restarts~ 🌸.
/// </remarks>
public interface ILinqHmacKeyProvider
{
    /// <summary>Gets the HMAC key bytes~ 🗝️.</summary>
    /// <returns>The key (>= 32 bytes recommended).</returns>
    byte[] GetKey();
}

