// <copyright file="ISpillProtector.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Streaming;

using System;

/// <summary>
/// 🔒 Phase 5.1.6 — encrypts spilled stream items at rest.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Spill writes workflow <b>data</b> — not configuration — to somewhere it wasn't
/// before. Whatever the stream carries (customer records, payment rows, PII) lands on disk or in a
/// bucket, so encryption is on by default rather than opt-in. A host may turn it off, but never
/// quietly: startup logs it, every spilling execution logs it, and the designer warns on stages
/// where spill could engage (plan §10, Q-D)~ 📢.
/// </para>
/// <para>
/// The abstraction lives here so <c>Workflow.Modules</c> stays free of the ASP.NET Data Protection
/// dependency — exactly the split already used for <c>IConnectionStringProtector</c>~ 🌸.
/// </para>
/// </remarks>
public interface ISpillProtector
{
    /// <summary>
    /// Gets a value indicating whether this protector actually encrypts~ 🔐.
    /// </summary>
    /// <remarks>
    /// <c>false</c> means spill is written in the clear. Callers use this to emit the required
    /// warnings — never to decide silently~ 📢.
    /// </remarks>
    bool IsEnabled { get; }

    /// <summary>
    /// Protects one spill record~ 🔒.
    /// </summary>
    /// <param name="plaintext">The serialized record.</param>
    /// <returns>The bytes to store.</returns>
    byte[] Protect(ReadOnlySpan<byte> plaintext);

    /// <summary>
    /// Reverses <see cref="Protect"/>~ 🔓.
    /// </summary>
    /// <param name="protectedBytes">The stored bytes.</param>
    /// <returns>The original serialized record.</returns>
    byte[] Unprotect(ReadOnlySpan<byte> protectedBytes);
}

/// <summary>
/// 🔓 A protector that stores spill in the clear.
/// </summary>
/// <remarks>
/// CopilotNote: This exists so "encryption is off" is an explicit, inspectable object rather than a
/// null check scattered through the spill code — and so <see cref="IsEnabled"/> gives the warning
/// paths something honest to read~ 📢
/// </remarks>
public sealed class NoOpSpillProtector : ISpillProtector
{
    /// <summary>Gets the shared instance~ ✨.</summary>
    public static NoOpSpillProtector Instance { get; } = new();

    /// <inheritdoc/>
    public bool IsEnabled => false;

    /// <inheritdoc/>
    public byte[] Protect(ReadOnlySpan<byte> plaintext) => plaintext.ToArray();

    /// <inheritdoc/>
    public byte[] Unprotect(ReadOnlySpan<byte> protectedBytes) => protectedBytes.ToArray();
}
