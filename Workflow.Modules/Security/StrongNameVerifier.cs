// <copyright file="StrongNameVerifier.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Security;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// 🔏 Phase 2.8.4 — Strong-name based <see cref="IAssemblyVerifier"/>: reads the public key token via
/// <see cref="AssemblyName.GetPublicKeyToken"/> and compares it against a configured trusted list.
/// Full Authenticode/X.509 chain validation is post-MVP (2.8.P2)~ ✨.
/// </summary>
public sealed class StrongNameVerifier : IAssemblyVerifier
{
    private readonly HashSet<string> trustedTokens;

    /// <summary>Initializes a new instance of the <see cref="StrongNameVerifier"/> class~ 🔏.</summary>
    /// <param name="trustedPublicKeyTokens">
    /// The configured trusted public key tokens (hex). When empty, any signed assembly is treated as
    /// trusted (no allow-list enforcement).
    /// </param>
    public StrongNameVerifier(IEnumerable<string>? trustedPublicKeyTokens = null)
    {
        this.trustedTokens = new HashSet<string>(
            (trustedPublicKeyTokens ?? Array.Empty<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(NormalizeToken),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public AssemblyVerificationResult Verify(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(assemblyPath);

        var messages = new List<string>();

        byte[]? tokenBytes;
        try
        {
            tokenBytes = AssemblyName.GetAssemblyName(assemblyPath).GetPublicKeyToken();
        }
        catch (Exception ex)
        {
            return new AssemblyVerificationResult(false, null, this.trustedTokens.Count == 0, new[] { $"Could not read assembly name: {ex.Message}" });
        }

        if (tokenBytes is null || tokenBytes.Length == 0)
        {
            messages.Add("Assembly is not strong-name signed.");
            return new AssemblyVerificationResult(false, null, this.trustedTokens.Count == 0, messages);
        }

        var token = Convert.ToHexString(tokenBytes).ToLowerInvariant();
        var trusted = this.trustedTokens.Count == 0 || this.trustedTokens.Contains(token);
        if (!trusted)
        {
            messages.Add($"Assembly public key token '{token}' is not in the trusted list.");
        }

        return new AssemblyVerificationResult(true, token, trusted, messages);
    }

    private static string NormalizeToken(string token)
        => token.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase).Replace(" ", string.Empty).ToLowerInvariant();
}
