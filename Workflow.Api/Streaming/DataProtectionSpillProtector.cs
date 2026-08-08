// <copyright file="DataProtectionSpillProtector.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Streaming;

using System;
using Microsoft.AspNetCore.DataProtection;
using Workflow.Modules.Streaming;

/// <summary>
/// 🔒 Phase 5.1.6 — <see cref="ISpillProtector"/> backed by ASP.NET Data Protection.
/// </summary>
/// <remarks>
/// CopilotNote: Same split as <see cref="Workflow.Api.Database.DataProtectionConnectionStringProtector"/> —
/// registered at the host so <c>Workflow.Modules</c> never takes a Data Protection dependency. The
/// purpose string binds spill ciphertext to this feature, so a spill blob can't be decrypted by the
/// connection-string protector or vice versa~ 🔐
/// </remarks>
public sealed class DataProtectionSpillProtector : ISpillProtector
{
    private const string Purpose = "Workflow.Streaming.Spill";

    private readonly IDataProtector protector;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataProtectionSpillProtector"/> class~ 🔐.
    /// </summary>
    /// <param name="provider">The Data Protection provider (from <c>AddDataProtection()</c>).</param>
    public DataProtectionSpillProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        this.protector = provider.CreateProtector(Purpose);
    }

    /// <inheritdoc/>
    public bool IsEnabled => true;

    /// <inheritdoc/>
    public byte[] Protect(ReadOnlySpan<byte> plaintext) => this.protector.Protect(plaintext.ToArray());

    /// <inheritdoc/>
    public byte[] Unprotect(ReadOnlySpan<byte> protectedBytes) => this.protector.Unprotect(protectedBytes.ToArray());
}
