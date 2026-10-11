// <copyright file="ScriptApprovalEndpoints.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.V1;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Workflow.Api.Auth;
using Workflow.Scripting.Approvals;
using Workflow.Scripting.Roslyn.Security;

/// <summary>
/// 🛡️ §9.2 — review of C# code that uses sensitive APIs. A denied compile records the exact code
/// as a <c>Candidate</c>; the author requests review by its hash; someone with the
/// <c>ScriptApprover</c> (or Admin) role approves or rejects it. Authors can't approve their own
/// requests. Approval covers that exact code only — any edit needs review again~ ✨.
/// </summary>
public static class ScriptApprovalEndpoints
{
    /// <summary>Registers the <c>/api/v1/script-approvals</c> endpoints.</summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapScriptApprovalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapV1Group().MapGroup("/script-approvals").WithTags("Script approvals");

        group.MapGet("/", ListHandler).WithName("ListScriptApprovals").RequireAuthorization(AuthConstants.ScriptApprovalReadPolicy);
        group.MapGet("/categories", CategoriesHandler).WithName("ListSensitiveApiCategories").RequireAuthorization(AuthConstants.ScriptApprovalReadPolicy);
        group.MapGet("/{hash}", GetHandler).WithName("GetScriptApproval").RequireAuthorization(AuthConstants.ScriptApprovalReadPolicy);
        group.MapPost("/{hash}/request", RequestHandler).WithName("RequestScriptApproval").RequireAuthorization(AuthConstants.WorkflowWritePolicy);
        group.MapPost("/{hash}/approve", (HttpContext http, string hash, ScriptApprovalNoteDto? body, IScriptApprovalStore store, CancellationToken ct)
            => DecideHandler(http, hash, body, store, approve: true, ct))
            .WithName("ApproveScript").RequireAuthorization(AuthConstants.ScriptApprovePolicy);
        group.MapPost("/{hash}/reject", (HttpContext http, string hash, ScriptApprovalNoteDto? body, IScriptApprovalStore store, CancellationToken ct)
            => DecideHandler(http, hash, body, store, approve: false, ct))
            .WithName("RejectScript").RequireAuthorization(AuthConstants.ScriptApprovePolicy);

        return app;
    }

    private static async Task<IResult> ListHandler(string? status, IScriptApprovalStore store, CancellationToken ct)
    {
        ScriptApprovalStatus? filter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<ScriptApprovalStatus>(status, ignoreCase: true, out var parsed))
            {
                var allowed = string.Join(", ", Enum.GetNames<ScriptApprovalStatus>());
                return ApiResults.BadRequestProblem($"Unknown status '{status}'. Use one of: {allowed}.");
            }

            filter = parsed;
        }

        var records = await store.ListAsync(filter, ct).ConfigureAwait(false);
        return Results.Ok(records.Select(ScriptApprovalDto.From).ToList());
    }

    private static IResult CategoriesHandler()
        => Results.Ok(SensitiveApiCatalog.All
            .Select(kv => new SensitiveApiCategoryDto(kv.Key, kv.Value.Title, kv.Value.Danger))
            .ToList());

    private static async Task<IResult> GetHandler(string hash, IScriptApprovalStore store, CancellationToken ct)
    {
        var record = await store.GetAsync(hash, ct).ConfigureAwait(false);
        return record is null
            ? ApiResults.NotFoundProblem($"No script approval record for hash '{hash}'.")
            : Results.Ok(ScriptApprovalDto.From(record));
    }

    private static async Task<IResult> RequestHandler(
        HttpContext http,
        string hash,
        ScriptApprovalNoteDto? body,
        IScriptApprovalStore store,
        CancellationToken ct)
    {
        var result = await store.RequestAsync(hash, http.ResolveCallerId(), body?.Note, ct).ConfigureAwait(false);
        return ToResult(result, hash);
    }

    private static async Task<IResult> DecideHandler(
        HttpContext http,
        string hash,
        ScriptApprovalNoteDto? body,
        IScriptApprovalStore store,
        bool approve,
        CancellationToken ct)
    {
        var result = await store.DecideAsync(hash, http.ResolveCallerId(), approve, body?.Note, ct).ConfigureAwait(false);
        return ToResult(result, hash);
    }

    private static IResult ToResult(ScriptApprovalResult result, string hash) => result.Outcome switch
    {
        ScriptApprovalOutcome.Ok => Results.Ok(ScriptApprovalDto.From(result.Record!)),
        ScriptApprovalOutcome.NotFound => ApiResults.NotFoundProblem($"No script approval record for hash '{hash}'."),
        ScriptApprovalOutcome.SelfApproval => ApiResults.ForbiddenProblem(
            "You requested this approval, so someone else has to review it."),
        _ => ApiResults.ConflictProblem(
            $"The record is '{result.Record?.Status}', which doesn't allow this. Only requested code can be approved or rejected, and approved or pending code can't be requested again."),
    };
}

/// <summary>An optional note on a request or decision.</summary>
/// <param name="Note">The author's justification or the reviewer's note.</param>
public sealed record ScriptApprovalNoteDto(string? Note);

/// <summary>A sensitive API category with its plain-language danger (for UI legends).</summary>
/// <param name="Category">The category id.</param>
/// <param name="Title">A short title.</param>
/// <param name="Danger">What an attacker could do with it.</param>
public sealed record SensitiveApiCategoryDto(string Category, string Title, string Danger);

/// <summary>A script approval record as returned by the API.</summary>
/// <param name="CodeHash">The approval hash.</param>
/// <param name="Kind">The code kind (<c>csharp-script</c>, <c>csharp-transform</c>, <c>linq</c>).</param>
/// <param name="Status">The review status.</param>
/// <param name="Code">The exact code that was analyzed.</param>
/// <param name="Flags">The sensitive APIs it uses, each with its danger explained.</param>
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
    IReadOnlyList<SensitiveApiFlag> Flags,
    DateTimeOffset FirstSeenAt,
    string? RequestedBy,
    DateTimeOffset? RequestedAt,
    string? RequestNote,
    string? DecidedBy,
    DateTimeOffset? DecidedAt,
    string? DecisionNote)
{
    /// <summary>Maps a store record to the DTO.</summary>
    /// <param name="r">The record.</param>
    /// <returns>The DTO.</returns>
    public static ScriptApprovalDto From(ScriptApprovalRecord r) => new(
        r.CodeHash, r.Kind, r.Status.ToString(), r.Code, r.Flags, r.FirstSeenAt,
        r.RequestedBy, r.RequestedAt, r.RequestNote, r.DecidedBy, r.DecidedAt, r.DecisionNote);
}
