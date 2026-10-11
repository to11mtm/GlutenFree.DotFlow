// <copyright file="ScriptApprovalModels.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Approvals;

using System;
using System.Collections.Generic;

/// <summary>Where a flagged C# script is in the review process (§9.2)~ 🛡️.</summary>
public enum ScriptApprovalStatus
{
    /// <summary>A compile was denied because of flags; nobody has asked for review yet.</summary>
    Candidate,

    /// <summary>The author asked for review.</summary>
    Requested,

    /// <summary>A reviewer approved this exact code; it may compile.</summary>
    Approved,

    /// <summary>A reviewer rejected this exact code.</summary>
    Rejected,
}

/// <summary>
/// One sensitive API a script resolves to, with the danger explained for reviewers and authors~ 🚩.
/// </summary>
/// <param name="Symbol">The resolved symbol, e.g. <c>System.IO.File.ReadAllText(string)</c>.</param>
/// <param name="Category">The category id, e.g. <c>Reflection</c>, <c>FileSystem</c>.</param>
/// <param name="Title">A short human title for the category.</param>
/// <param name="Danger">What an attacker could do with it — shown in the UI next to the flag.</param>
/// <param name="Line">1-based line in the analyzed code, when known.</param>
/// <param name="Column">1-based column in the analyzed code, when known.</param>
public sealed record SensitiveApiFlag(
    string Symbol,
    string Category,
    string Title,
    string Danger,
    int? Line = null,
    int? Column = null);

/// <summary>
/// The review record for one exact piece of code, identified by its content hash~ 📋.
/// </summary>
/// <param name="CodeHash">SHA-256 of the kind + normalized code (see <c>ScriptCodeHash</c>).</param>
/// <param name="Kind">What kind of code it is: <c>csharp-script</c>, <c>csharp-transform</c>, <c>linq</c>.</param>
/// <param name="Code">The exact code that was analyzed (including any prepended script libraries).</param>
/// <param name="Flags">Every sensitive API the code resolves to.</param>
/// <param name="Status">The review status.</param>
/// <param name="FirstSeenAt">When a compile first flagged this code.</param>
/// <param name="RequestedBy">Caller id of the author who asked for review.</param>
/// <param name="RequestedAt">When review was requested.</param>
/// <param name="RequestNote">The author's justification.</param>
/// <param name="DecidedBy">Caller id of the reviewer who approved/rejected.</param>
/// <param name="DecidedAt">When the decision was made.</param>
/// <param name="DecisionNote">The reviewer's note.</param>
public sealed record ScriptApprovalRecord(
    string CodeHash,
    string Kind,
    string Code,
    IReadOnlyList<SensitiveApiFlag> Flags,
    ScriptApprovalStatus Status,
    DateTimeOffset FirstSeenAt,
    string? RequestedBy = null,
    DateTimeOffset? RequestedAt = null,
    string? RequestNote = null,
    string? DecidedBy = null,
    DateTimeOffset? DecidedAt = null,
    string? DecisionNote = null);

/// <summary>Why a request/decision on an approval record did or didn't go through~ 🚦.</summary>
public enum ScriptApprovalOutcome
{
    /// <summary>The change was applied.</summary>
    Ok,

    /// <summary>No record with that hash.</summary>
    NotFound,

    /// <summary>The record isn't in a state that allows this change (e.g. approving a candidate nobody requested).</summary>
    InvalidState,

    /// <summary>The reviewer is the author who requested it — self-approval is not allowed.</summary>
    SelfApproval,
}

/// <summary>The result of a request/decision: the outcome plus the record as it now stands~ 📬.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Record">The record after the change (null when not found).</param>
public sealed record ScriptApprovalResult(ScriptApprovalOutcome Outcome, ScriptApprovalRecord? Record);
