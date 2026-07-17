// <copyright file="IConnectionStringProtector.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Abstractions;

/// <summary>
/// 🔒 Encrypts/decrypts connection strings for at-rest protection in the persisted
/// connection registry (2.4.a.5)~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: This abstraction keeps the ASP.NET <c>IDataProtector</c> dependency out of the
/// persistence + modules layers — the host (Workflow.Api) supplies a Data-Protection-backed
/// implementation, while tests and non-web hosts can use a simple reversible or no-op protector.
/// The in-memory registry never protects (config values are plain by design, D3)~ 🌸.
/// </para>
/// </remarks>
public interface IConnectionStringProtector
{
    /// <summary>Encrypts a plaintext connection string for storage~ 🔐.</summary>
    /// <param name="plaintext">The plaintext connection string.</param>
    /// <returns>The protected (ciphertext) form.</returns>
    string Protect(string plaintext);

    /// <summary>Decrypts a stored connection string~ 🔓.</summary>
    /// <param name="protectedValue">The protected (ciphertext) form.</param>
    /// <returns>The plaintext connection string.</returns>
    string Unprotect(string protectedValue);
}

/// <summary>
/// 🪶 Pass-through protector — stores connection strings verbatim. The default when no
/// host-supplied protector is registered (e.g. dev/tests without Data Protection)~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: Not for production secrets — register a real <see cref="IConnectionStringProtector"/>
/// (Data-Protection-backed) at the host to encrypt at rest. This exists so the persisted registry
/// works out of the box without hard-requiring ASP.NET Data Protection~ 🌸.
/// </remarks>
public sealed class NoOpConnectionStringProtector : IConnectionStringProtector
{
    /// <inheritdoc/>
    public string Protect(string plaintext) => plaintext;

    /// <inheritdoc/>
    public string Unprotect(string protectedValue) => protectedValue;
}

