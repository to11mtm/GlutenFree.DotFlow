// <copyright file="IScriptApprovalStore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Approvals;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// The compile-time side of script approval (§9.2): synchronous so the C# compilers can call it
/// inline. Fail closed — when no gate is registered, use <see cref="DenyAllScriptApprovalGate"/>~ 🚧.
/// </summary>
public interface IScriptApprovalGate
{
    /// <summary>Whether this exact code (by hash) has been approved by a reviewer.</summary>
    /// <param name="codeHash">The code hash.</param>
    /// <returns><c>true</c> only for an <see cref="ScriptApprovalStatus.Approved"/> record.</returns>
    bool IsApproved(string codeHash);

    /// <summary>
    /// Records code a compile just denied, so the author can request review of exactly what ran.
    /// Never downgrades an existing record. Must not throw.
    /// </summary>
    /// <param name="codeHash">The code hash.</param>
    /// <param name="kind">The code kind (<c>csharp-script</c>, <c>csharp-transform</c>, <c>linq</c>).</param>
    /// <param name="code">The exact analyzed code.</param>
    /// <param name="flags">The sensitive APIs it resolves to.</param>
    void RecordCandidate(string codeHash, string kind, string code, IReadOnlyList<SensitiveApiFlag> flags);
}

/// <summary>The review side of script approval: list, request, approve, reject~ 📋.</summary>
public interface IScriptApprovalStore : IScriptApprovalGate
{
    /// <summary>Lists records, newest first, optionally filtered by status.</summary>
    /// <param name="status">Only this status, or all when null.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The records.</returns>
    Task<IReadOnlyList<ScriptApprovalRecord>> ListAsync(ScriptApprovalStatus? status = null, CancellationToken ct = default);

    /// <summary>Gets one record by hash.</summary>
    /// <param name="codeHash">The code hash.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The record, or null.</returns>
    Task<ScriptApprovalRecord?> GetAsync(string codeHash, CancellationToken ct = default);

    /// <summary>The author asks for review of a candidate (or re-asks after a rejection).</summary>
    /// <param name="codeHash">The code hash.</param>
    /// <param name="requestedBy">The author's caller id.</param>
    /// <param name="note">Why the script needs these APIs.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The outcome and record.</returns>
    Task<ScriptApprovalResult> RequestAsync(string codeHash, string requestedBy, string? note, CancellationToken ct = default);

    /// <summary>A reviewer approves or rejects a requested record. The reviewer can't be the requester.</summary>
    /// <param name="codeHash">The code hash.</param>
    /// <param name="decidedBy">The reviewer's caller id.</param>
    /// <param name="approve"><c>true</c> to approve, <c>false</c> to reject.</param>
    /// <param name="note">The reviewer's note.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The outcome and record.</returns>
    Task<ScriptApprovalResult> DecideAsync(string codeHash, string decidedBy, bool approve, string? note, CancellationToken ct = default);
}

/// <summary>Fail-closed default: nothing flagged is ever approved, and nothing is recorded~ 🚫.</summary>
public sealed class DenyAllScriptApprovalGate : IScriptApprovalGate
{
    /// <summary>Gets the shared instance.</summary>
    public static DenyAllScriptApprovalGate Instance { get; } = new();

    /// <inheritdoc/>
    public bool IsApproved(string codeHash) => false;

    /// <inheritdoc/>
    public void RecordCandidate(string codeHash, string kind, string code, IReadOnlyList<SensitiveApiFlag> flags)
    {
    }
}

/// <summary>
/// Persistence seam for the approval catalog — the host adapts it over its blob store, like
/// <see cref="Libraries.IScriptLibraryPersistence"/>~ 🗄️.
/// </summary>
public interface IScriptApprovalPersistence
{
    /// <summary>Reads the serialized catalog, or null when none exists yet.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The JSON, or null.</returns>
    Task<string?> ReadAsync(CancellationToken ct = default);

    /// <summary>Writes the serialized catalog.</summary>
    /// <param name="json">The JSON.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    Task WriteAsync(string json, CancellationToken ct = default);
}
