// <copyright file="DataProtectionConnectionStringProtector.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Database;

using Microsoft.AspNetCore.DataProtection;
using Workflow.Modules.Database.Abstractions;

/// <summary>
/// 🔒 Phase 2.4.a.5 — <see cref="IConnectionStringProtector"/> backed by ASP.NET Data Protection~ ✨.
/// Encrypts persisted connection strings at rest using a purpose-scoped <see cref="IDataProtector"/>.
/// </summary>
/// <remarks>
/// CopilotNote: Registered at the host so the persistence + modules layers stay free of the
/// ASP.NET Data Protection dependency (they only see <see cref="IConnectionStringProtector"/>).
/// The purpose string binds ciphertext to this feature — keys live wherever Data Protection is
/// configured (file system / DPAPI / Azure KV, etc.)~ 🌸.
/// </remarks>
public sealed class DataProtectionConnectionStringProtector : IConnectionStringProtector
{
    private const string Purpose = "Workflow.Modules.Database.ConnectionString";

    private readonly IDataProtector protector;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataProtectionConnectionStringProtector"/> class~ 🔐.
    /// </summary>
    /// <param name="provider">The Data Protection provider (from <c>AddDataProtection()</c>).</param>
    public DataProtectionConnectionStringProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        this.protector = provider.CreateProtector(Purpose);
    }

    /// <inheritdoc/>
    public string Protect(string plaintext) => this.protector.Protect(plaintext);

    /// <inheritdoc/>
    public string Unprotect(string protectedValue) => this.protector.Unprotect(protectedValue);
}

