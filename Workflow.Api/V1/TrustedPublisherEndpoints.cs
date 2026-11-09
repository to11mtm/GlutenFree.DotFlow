// <copyright file="TrustedPublisherEndpoints.cs" company="GlutenFree">
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
using Workflow.Api.Modules;

/// <summary>
/// 🔏 Stage 7.4 — the Trusted publishers page's API:
/// <list type="bullet">
///   <item><c>GET /settings/module-publishers</c> — the trusted publishers and CA roots, the server's ones locked (WorkflowRead).</item>
///   <item><c>PUT /settings/module-publishers</c> — save the editable ones (Admin; validated).</item>
///   <item><c>GET /settings/module-publishers/audit</c> — who changed what (Admin).</item>
/// </list>
/// </summary>
public static class TrustedPublisherEndpoints
{
    /// <summary>Maps the endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same builder.</returns>
    public static IEndpointRouteBuilder MapTrustedPublisherEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapV1Group().MapGroup("/settings/module-publishers").WithTags("Trusted publishers");

        group.MapGet("/", (TrustedPublisherService service) => Results.Ok(service.View()))
            .WithName("GetTrustedPublishers")
            .RequireAuthorization(AuthConstants.WorkflowReadPolicy);

        group.MapPut("/", SaveHandler)
            .WithName("SaveTrustedPublishers")
            .RequireAuthorization(AuthConstants.AdminPolicy);

        group.MapGet("/audit", (TrustedPublisherService service) => Results.Ok(service.Audit()))
            .WithName("GetTrustedPublishersAudit")
            .RequireAuthorization(AuthConstants.AdminPolicy);

        return app;
    }

    private static async Task<IResult> SaveHandler(HttpContext http, TrustedPublishersSave body, TrustedPublisherService service, CancellationToken ct)
    {
        var caller = http.User.Identity?.Name ?? "anonymous";
        var errors = await service.SaveAsync(body, caller, ct).ConfigureAwait(false);
        return errors.Count == 0
            ? Results.Ok(service.View())
            : Results.ValidationProblem(new Dictionary<string, string[]> { ["publishers"] = [.. errors] });
    }
}
