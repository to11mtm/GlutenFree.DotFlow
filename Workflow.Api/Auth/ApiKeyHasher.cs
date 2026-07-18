// <copyright file="ApiKeyHasher.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Auth;

using System;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// 🔑 Phase 2.7.7 — One-way hashing for API keys. Keys are compared by SHA-256 hash so the raw
/// key never needs to be stored~ ✨. Use <see cref="Hash"/> as a dev helper to produce the value
/// placed in <c>Api:Auth:ApiKeys[].KeyHash</c>.
/// </summary>
public static class ApiKeyHasher
{
    /// <summary>Computes the base64 SHA-256 hash of a raw API key~ 🔑.</summary>
    /// <param name="rawKey">The raw API key.</param>
    /// <returns>The base64-encoded SHA-256 hash.</returns>
    public static string Hash(string rawKey)
    {
        ArgumentNullException.ThrowIfNull(rawKey);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToBase64String(bytes);
    }

    /// <summary>Constant-time comparison of a raw key against a stored hash~ 🛡️.</summary>
    /// <param name="rawKey">The presented raw key.</param>
    /// <param name="storedHash">The stored base64 SHA-256 hash.</param>
    /// <returns><c>true</c> when the key matches.</returns>
    public static bool Verify(string rawKey, string storedHash)
    {
        if (string.IsNullOrEmpty(rawKey) || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        var computed = Encoding.UTF8.GetBytes(Hash(rawKey));
        var expected = Encoding.UTF8.GetBytes(storedHash);
        return CryptographicOperations.FixedTimeEquals(computed, expected);
    }
}
