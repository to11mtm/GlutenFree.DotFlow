// <copyright file="ConfigureJwtBearerOptions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Auth;

using System;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

/// <summary>
/// 🎫 Phase 2.7.7 — Lazily configures the JWT bearer scheme from the live <see cref="ApiAuthOptions"/>
/// so token validation honours configuration applied after service registration~ ✨.
/// </summary>
public sealed class ConfigureJwtBearerOptions : IConfigureNamedOptions<JwtBearerOptions>
{
    private readonly IOptionsMonitor<ApiAuthOptions> authOptions;

    /// <summary>Initializes a new instance of the <see cref="ConfigureJwtBearerOptions"/> class~ 🎫.</summary>
    /// <param name="authOptions">The live API auth options.</param>
    public ConfigureJwtBearerOptions(IOptionsMonitor<ApiAuthOptions> authOptions)
    {
        this.authOptions = authOptions;
    }

    /// <inheritdoc/>
    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme)
        {
            return;
        }

        this.Configure(options);
    }

    /// <inheritdoc/>
    public void Configure(JwtBearerOptions options)
    {
        var jwt = this.authOptions.CurrentValue.Jwt;

        if (!string.IsNullOrWhiteSpace(jwt.Authority))
        {
            options.Authority = jwt.Authority;
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(jwt.Issuer),
            ValidIssuer = jwt.Issuer,
            ValidateAudience = !string.IsNullOrWhiteSpace(jwt.Audience),
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = !string.IsNullOrWhiteSpace(jwt.SigningKey),
            IssuerSigningKey = string.IsNullOrWhiteSpace(jwt.SigningKey)
                ? null
                : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    }
}
