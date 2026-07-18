// <copyright file="CallerIdentity.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Auth;

using System.Security.Claims;
using Microsoft.AspNetCore.Http;

/// <summary>
/// 🪪 Resolves the caller identity for audit fields (Phase 2.7.0 — extracted from Program.cs)~ ✨.
/// </summary>
/// <remarks>
/// Resolution order: an explicit <c>X-Caller-Id</c> header override → the authenticated principal's
/// <see cref="ClaimTypes.NameIdentifier"/>/<c>sub</c>/name claim → the <c>"system"</c> fallback.
/// Once auth (2.7.7) lands the claim path becomes the primary source~ 🌸.
/// </remarks>
public static class CallerIdentity
{
    /// <summary>The fallback caller id used when nothing else resolves~ 🤖.</summary>
    public const string SystemCaller = "system";

    /// <summary>
    /// Resolves the caller id for the current request~ 🪪.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The resolved caller id (never <c>null</c>).</returns>
    public static string ResolveCallerId(this HttpContext context)
    {
        // Authenticated principal wins (2.7.7) — the key/token identity is authoritative~
        var user = context.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var claim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst("sub")?.Value
                ?? user.Identity?.Name;

            if (!string.IsNullOrWhiteSpace(claim))
            {
                return claim;
            }
        }

        // Unauthenticated/dev: honour an explicit X-Caller-Id override~
        if (context.Request.Headers.TryGetValue("X-Caller-Id", out var headerValue)
            && !string.IsNullOrWhiteSpace(headerValue))
        {
            return headerValue.ToString();
        }

        return SystemCaller;
    }
}
