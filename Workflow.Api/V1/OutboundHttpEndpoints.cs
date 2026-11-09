// <copyright file="OutboundHttpEndpoints.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.V1;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Workflow.Api.Auth;
using Workflow.Api.Http;
using Workflow.Core.Http;

/// <summary>
/// 🌐 Stage 7.3 — the outbound HTTP setup screen's API:
/// <list type="bullet">
///   <item><c>GET /settings/outbound-http</c> — every channel's effective, saved and locked values (WorkflowRead).</item>
///   <item><c>PUT /settings/outbound-http</c> — save the policy (Admin; locked fields are refused).</item>
///   <item><c>POST /settings/outbound-http/check</c> — "would this URL be allowed?" (WorkflowWrite).</item>
///   <item><c>GET /settings/outbound-http/audit</c> — who changed what (Admin).</item>
/// </list>
/// </summary>
public static class OutboundHttpEndpoints
{
    /// <summary>Maps the endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same builder.</returns>
    public static IEndpointRouteBuilder MapOutboundHttpEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapV1Group().MapGroup("/settings/outbound-http").WithTags("Outbound HTTP");

        group.MapGet("/", (OutboundHttpPolicyService service) => Results.Ok(service.View()))
            .WithName("GetOutboundHttpPolicy")
            .RequireAuthorization(AuthConstants.WorkflowReadPolicy);

        group.MapPut("/", SaveHandler)
            .WithName("SaveOutboundHttpPolicy")
            .RequireAuthorization(AuthConstants.AdminPolicy);

        group.MapPost("/check", (OutboundHttpCheckRequest body, OutboundHttpPolicyService service, CancellationToken ct)
                => CheckHandler(body, service, ct))
            .WithName("CheckOutboundHttp")
            .RequireAuthorization(AuthConstants.WorkflowWritePolicy);

        group.MapGet("/audit", (OutboundHttpPolicyService service) => Results.Ok(service.Audit()))
            .WithName("GetOutboundHttpAudit")
            .RequireAuthorization(AuthConstants.AdminPolicy);

        return app;
    }

    private static async Task<IResult> SaveHandler(HttpContext http, OutboundHttpOptions body, OutboundHttpPolicyService service, CancellationToken ct)
    {
        var caller = http.User.Identity?.Name ?? "anonymous";
        var errors = await service.SaveAsync(body, caller, ct).ConfigureAwait(false);
        return errors.Count == 0
            ? Results.Ok(service.View())
            : Results.ValidationProblem(new Dictionary<string, string[]> { ["policy"] = [.. errors] });
    }

    private static async Task<IResult> CheckHandler(OutboundHttpCheckRequest body, OutboundHttpPolicyService service, CancellationToken ct)
        => Results.Ok(await service.CheckAsync(body.Channel, body.Url, body.AllowedHosts, ct).ConfigureAwait(false));
}

/// <summary>🌐 Stage 7.3 — a "would this be allowed?" request.</summary>
/// <param name="Channel">The channel.</param>
/// <param name="Url">The URL.</param>
/// <param name="AllowedHosts">Per-node narrowing to simulate (optional).</param>
public sealed record OutboundHttpCheckRequest(OutboundHttpChannel Channel, string Url, IReadOnlyList<string>? AllowedHosts = null);
