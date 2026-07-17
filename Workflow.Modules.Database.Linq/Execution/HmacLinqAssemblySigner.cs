// <copyright file="HmacLinqAssemblySigner.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq.Execution;

using System;
using System.Security.Cryptography;
using Workflow.Modules.Database.Linq.Abstractions;

/// <summary>
/// 🔒 HMAC-SHA256 signer — prepends a 32-byte tag to the assembly bytes; verifies + strips on read (2.4.b.2)~ ✨.
/// </summary>
public sealed class HmacLinqAssemblySigner : ILinqAssemblySigner
{
    private const int TagLength = 32; // HMAC-SHA256 output size

    private readonly byte[] key;

    /// <summary>Initializes a new instance of the <see cref="HmacLinqAssemblySigner"/> class~ 🔐.</summary>
    /// <param name="keyProvider">Supplies the HMAC key.</param>
    public HmacLinqAssemblySigner(ILinqHmacKeyProvider keyProvider)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);
        this.key = keyProvider.GetKey();
        if (this.key is null || this.key.Length == 0)
        {
            throw new ArgumentException("HMAC key must be non-empty~ 💔", nameof(keyProvider));
        }
    }

    /// <inheritdoc/>
    public byte[] Sign(byte[] assemblyBytes)
    {
        ArgumentNullException.ThrowIfNull(assemblyBytes);

        var tag = this.ComputeTag(assemblyBytes);
        var payload = new byte[TagLength + assemblyBytes.Length];
        Buffer.BlockCopy(tag, 0, payload, 0, TagLength);
        Buffer.BlockCopy(assemblyBytes, 0, payload, TagLength, assemblyBytes.Length);
        return payload;
    }

    /// <inheritdoc/>
    public bool TryVerify(byte[] signedPayload, out byte[] assemblyBytes)
    {
        assemblyBytes = Array.Empty<byte>();
        if (signedPayload is null || signedPayload.Length < TagLength)
        {
            return false;
        }

        var storedTag = new byte[TagLength];
        Buffer.BlockCopy(signedPayload, 0, storedTag, 0, TagLength);

        var body = new byte[signedPayload.Length - TagLength];
        Buffer.BlockCopy(signedPayload, TagLength, body, 0, body.Length);

        var expected = this.ComputeTag(body);

        // Constant-time compare — no early-out on the first mismatched byte.
        if (!CryptographicOperations.FixedTimeEquals(storedTag, expected))
        {
            return false;
        }

        assemblyBytes = body;
        return true;
    }

    private byte[] ComputeTag(byte[] body)
    {
        using var hmac = new HMACSHA256(this.key);
        return hmac.ComputeHash(body);
    }
}

/// <summary>
/// 🗝️ Default ephemeral HMAC key — random per process. Safe (no stable secret to leak) but the
/// on-disk cache is unusable after a restart (a one-time recompile). The host may replace this with a
/// Data-Protection-backed stable key so the cache survives restarts~ 🌸.
/// </summary>
public sealed class EphemeralLinqHmacKeyProvider : ILinqHmacKeyProvider
{
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);

    /// <inheritdoc/>
    public byte[] GetKey() => this.key;
}

