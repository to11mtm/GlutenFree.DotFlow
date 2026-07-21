// <copyright file="RolePolicyRequirement.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Auth;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

/// <summary>
/// 🛡️ Phase 2.7.7 — An authorization requirement that grants access to any of the named roles,
/// but becomes a no-op when <c>Api:Auth:Require</c> is disabled (dev/anonymous-friendly)~ ✨.
/// </summary>
/// <remarks>
/// The <c>Require</c> flag is read at evaluation time via <see cref="IOptionsMonitor{TOptions}"/> so
/// it honours configuration applied after service registration (e.g. test hosts / reloads)~ 🌸.
/// </remarks>
public sealed class RolePolicyRequirement : IAuthorizationRequirement
{
    /// <summary>Initializes a new instance of the <see cref="RolePolicyRequirement"/> class~ 🛡️.</summary>
    /// <param name="allowedRoles">The roles that satisfy this requirement.</param>
    public RolePolicyRequirement(params string[] allowedRoles)
    {
        this.AllowedRoles = allowedRoles ?? Array.Empty<string>();
    }

    /// <summary>Gets the roles that satisfy this requirement.</summary>
    public string[] AllowedRoles { get; }
}

/// <summary>
/// 🛡️ Phase 2.7.7 — Evaluates <see cref="RolePolicyRequirement"/> against the current principal,
/// reading the live <see cref="ApiAuthOptions.Require"/> flag~ ✨.
/// </summary>
public sealed class RolePolicyHandler : AuthorizationHandler<RolePolicyRequirement>
{
    private readonly IOptionsMonitor<ApiAuthOptions> options;

    /// <summary>Initializes a new instance of the <see cref="RolePolicyHandler"/> class~ 🛡️.</summary>
    /// <param name="options">The live auth options.</param>
    public RolePolicyHandler(IOptionsMonitor<ApiAuthOptions> options)
    {
        this.options = options;
    }

    /// <inheritdoc/>
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RolePolicyRequirement requirement)
    {
        // Auth disabled → allow everything (the authorization middleware won't challenge)~
        if (!this.options.CurrentValue.Require)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var user = context.User;
        if (user?.Identity?.IsAuthenticated == true
            && (requirement.AllowedRoles.Length == 0 || requirement.AllowedRoles.Any(user.IsInRole)))
        {
            context.Succeed(requirement);
        }

        // Otherwise leave unsatisfied — the middleware issues 401 (anonymous) or 403 (wrong role)~
        return Task.CompletedTask;
    }
}
