// <copyright file="HmacScriptAssemblySigner.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Roslyn.Execution;

using System;
using System.Security.Cryptography;
using Workflow.Scripting.Roslyn.Abstractions;

/// <summary>
/// 🔑 Ephemeral per-process HMAC key provider (safe default)~ ✨.
/// </summary>
public sealed class EphemeralScriptHmacKeyProvider : IScriptHmacKeyProvider
{
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);

    /// <inheritdoc/>
    public byte[] GetKey() => this.key;
}

/// <summary>
/// 🔏 HMAC-SHA256 signer — prepends a 32-byte tag on <see cref="Sign"/>, verifies + strips on
/// <see cref="TryVerify"/> (tamper → verification fails → cache miss, never handed to the loader)~ ✨.
/// </summary>
public sealed class HmacScriptAssemblySigner : IScriptAssemblySigner
{
    private const int TagLength = 32;
    private readonly IScriptHmacKeyProvider keyProvider;

    /// <summary>Initializes a new instance of the <see cref="HmacScriptAssemblySigner"/> class~ 🔏.</summary>
    /// <param name="keyProvider">The HMAC key provider.</param>
    public HmacScriptAssemblySigner(IScriptHmacKeyProvider keyProvider)
    {
        this.keyProvider = keyProvider;
    }

    /// <inheritdoc/>
    public byte[] Sign(byte[] assemblyBytes)
    {
        ArgumentNullException.ThrowIfNull(assemblyBytes);
        var tag = HMACSHA256.HashData(this.keyProvider.GetKey(), assemblyBytes);
        var result = new byte[TagLength + assemblyBytes.Length];
        Buffer.BlockCopy(tag, 0, result, 0, TagLength);
        Buffer.BlockCopy(assemblyBytes, 0, result, TagLength, assemblyBytes.Length);
        return result;
    }

    /// <inheritdoc/>
    public bool TryVerify(byte[] taggedBytes, out byte[] assemblyBytes)
    {
        assemblyBytes = Array.Empty<byte>();
        if (taggedBytes is null || taggedBytes.Length < TagLength)
        {
            return false;
        }

        var payload = new byte[taggedBytes.Length - TagLength];
        Buffer.BlockCopy(taggedBytes, TagLength, payload, 0, payload.Length);

        var expected = new byte[TagLength];
        Buffer.BlockCopy(taggedBytes, 0, expected, 0, TagLength);
        var actual = HMACSHA256.HashData(this.keyProvider.GetKey(), payload);

        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            return false;
        }

        assemblyBytes = payload;
        return true;
    }
}
