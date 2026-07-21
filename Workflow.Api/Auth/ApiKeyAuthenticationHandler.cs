// <copyright file="ApiKeyAuthenticationHandler.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Auth;

using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// 🔑 Phase 2.7.7 — Options for the API-key authentication scheme (no state — keys are read from the
/// live <see cref="ApiAuthOptions"/>)~ ✨.
/// </summary>
public sealed class ApiKeyOptions : AuthenticationSchemeOptions
{
}

/// <summary>
/// 🔑 Phase 2.7.7 — Validates the <c>X-API-Key</c> header against the configured, hashed keys and
/// builds a <see cref="ClaimsPrincipal"/> with the key's caller id + roles~ ✨💖.
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyOptions>
{
    private readonly IOptionsMonitor<ApiAuthOptions> authOptions;

    /// <summary>Initializes a new instance of the <see cref="ApiKeyAuthenticationHandler"/> class~ 🔑.</summary>
    /// <param name="options">The scheme options monitor.</param>
    /// <param name="logger">The logger factory.</param>
    /// <param name="encoder">The URL encoder.</param>
    /// <param name="authOptions">The live API auth options (key set).</param>
    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptionsMonitor<ApiAuthOptions> authOptions)
        : base(options, logger, encoder)
    {
        this.authOptions = authOptions;
    }

    /// <inheritdoc/>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!this.Request.Headers.TryGetValue(AuthConstants.ApiKeyHeader, out var headerValues))
        {
            // No key presented — stay anonymous (the authorization layer decides if that's allowed)~
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var presented = headerValues.ToString();
        if (string.IsNullOrWhiteSpace(presented))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var keys = this.authOptions.CurrentValue.ApiKeys;
        var match = keys.FirstOrDefault(k => ApiKeyHasher.Verify(presented, k.KeyHash));
        if (match is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, match.CallerId) };
        claims.AddRange(match.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var identity = new ClaimsIdentity(claims, AuthConstants.ApiKeyScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, AuthConstants.ApiKeyScheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
