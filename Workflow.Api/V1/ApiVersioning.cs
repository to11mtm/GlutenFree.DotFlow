// <copyright file="ApiVersioning.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.V1;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>
/// 🔢 API versioning helpers for the v1 resource surface (Phase 2.7.0)~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: For the single-version MVP we use a plain <c>/api/v1</c> route prefix + a
/// <c>api-supported-versions</c> response header rather than the full <c>Asp.Versioning</c> library —
/// no new dependency, and trivially extensible when v2 lands (introduce the library then)~ 🌸.
/// </remarks>
public static class ApiVersioning
{
    /// <summary>The supported API versions advertised on every v1 response~ 🏷️.</summary>
    public const string SupportedVersions = "1.0";

    /// <summary>
    /// Creates the <c>/api/v1</c> route group with the supported-versions header applied~ 🔢.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The v1 route group builder.</returns>
    public static RouteGroupBuilder MapV1Group(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1");
        group.AddEndpointFilter(async (ctx, next) =>
        {
            ctx.HttpContext.Response.Headers["api-supported-versions"] = SupportedVersions;
            return await next(ctx).ConfigureAwait(false);
        });
        return group;
    }
}
