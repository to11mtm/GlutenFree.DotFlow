// <copyright file="ScriptApprovalDtos.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Api.Dtos;

using System;
using System.Collections.Generic;

/// <summary>
/// 🚩 §9.2 — Client mirror of <c>SensitiveApiFlag</c>: one sensitive API a C# script resolves to,
/// with the danger explained for the author and the reviewer~ ✨.
/// </summary>
/// <param name="Symbol">The resolved symbol, e.g. <c>System.IO.File.Exists(string)</c>.</param>
/// <param name="Category">The category id (<c>Reflection</c>, <c>FileSystem</c>, …).</param>
/// <param name="Title">A short title for the category.</param>
/// <param name="Danger">What an attacker could do with it.</param>
/// <param name="Line">1-based line in the code, when known.</param>
/// <param name="Column">1-based column in the code, when known.</param>
public sealed record SensitiveApiFlagDto(
    string Symbol,
    string Category,
    string Title,
    string Danger,
    int? Line = null,
    int? Column = null);

/// <summary>🛡️ §9.2 — Client mirror of <c>ScriptApprovalDto</c>: the review record for one exact piece of code.</summary>
/// <param name="CodeHash">The approval hash.</param>
/// <param name="Kind">The code kind (<c>csharp-script</c>, <c>csharp-transform</c>, <c>linq</c>).</param>
/// <param name="Status"><c>Candidate</c>, <c>Requested</c>, <c>Approved</c> or <c>Rejected</c>.</param>
/// <param name="Code">The exact code that was analyzed.</param>
/// <param name="Flags">The sensitive APIs it uses.</param>
/// <param name="FirstSeenAt">When a compile first flagged it.</param>
/// <param name="RequestedBy">Who asked for review.</param>
/// <param name="RequestedAt">When review was requested.</param>
/// <param name="RequestNote">The author's justification.</param>
/// <param name="DecidedBy">Who approved/rejected.</param>
/// <param name="DecidedAt">When it was decided.</param>
/// <param name="DecisionNote">The reviewer's note.</param>
public sealed record ScriptApprovalDto(
    string CodeHash,
    string Kind,
    string Status,
    string Code,
    List<SensitiveApiFlagDto> Flags,
    DateTimeOffset FirstSeenAt,
    string? RequestedBy,
    DateTimeOffset? RequestedAt,
    string? RequestNote,
    string? DecidedBy,
    DateTimeOffset? DecidedAt,
    string? DecisionNote);

/// <summary>📝 §9.2 — An optional note on a request or decision.</summary>
/// <param name="Note">The note.</param>
public sealed record ScriptApprovalNoteDto(string? Note);
