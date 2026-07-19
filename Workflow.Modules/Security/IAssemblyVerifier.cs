// <copyright file="IAssemblyVerifier.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Security;

using System.Collections.Generic;

/// <summary>
/// 🔏 Phase 2.8.4 — Optional trust gate for loaded plugin assemblies. Reports whether an assembly
/// is signed, its public key token, and whether it is trusted per configuration~ ✨.
/// </summary>
public interface IAssemblyVerifier
{
    /// <summary>Verifies an assembly file's signature/trust~ 🔏.</summary>
    /// <param name="assemblyPath">The path to the assembly DLL.</param>
    /// <returns>The verification result.</returns>
    AssemblyVerificationResult Verify(string assemblyPath);
}

/// <summary>
/// 🔏 Phase 2.8.4 — The outcome of verifying an assembly~ ✨.
/// </summary>
/// <param name="Signed">Whether the assembly carries a strong-name public key.</param>
/// <param name="PublicKeyToken">The public key token (hex), or <c>null</c> when unsigned.</param>
/// <param name="Trusted">Whether the token is in the configured trusted list (or the list is empty).</param>
/// <param name="Messages">Human-readable advisories (warnings).</param>
public sealed record AssemblyVerificationResult(
    bool Signed,
    string? PublicKeyToken,
    bool Trusted,
    IReadOnlyList<string> Messages);
