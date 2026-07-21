// <copyright file="SignalRTestHarness.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.RealTime;

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Workflow.Api.Auth;

/// <summary>
/// 📡 Phase 3.2 — A <see cref="WebApplicationFactory{TEntryPoint}"/> that hosts the API with
/// in-memory SQLite persistence and (optionally) enforced auth, plus a helper to build a
/// <see cref="HubConnection"/> to <c>WorkflowHub</c> over the in-memory test server~ ✨.
/// </summary>
public sealed class SignalRTestHarness : WebApplicationFactory<Program>
{
    public const string JwtSigningKey = "realtime-test-signing-key-that-is-at-least-32-bytes!!";
    public const string JwtIssuer = "dotflow-tests";
    public const string JwtAudience = "dotflow-api";

    private readonly bool requireAuth;
    private readonly string[] allowedOrigins;

    /// <summary>Initializes a new instance of the <see cref="SignalRTestHarness"/> class~ 📡.</summary>
    /// <param name="requireAuth">Whether to enforce authentication (<c>Api:Auth:Require</c>).</param>
    /// <param name="allowedOrigins">CORS allowed origins for the hub.</param>
    public SignalRTestHarness(bool requireAuth = false, string[]? allowedOrigins = null)
    {
        this.requireAuth = requireAuth;
        this.allowedOrigins = allowedOrigins ?? Array.Empty<string>();
    }

    /// <summary>Mints a signed JWT with the given role(s)~ 🎫.</summary>
    /// <param name="role">The role claim value (e.g. <see cref="AuthConstants.AdminRole"/>).</param>
    /// <returns>The compact JWT string.</returns>
    public static string MakeJwt(string role)
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, "hub-user"),
                new Claim(ClaimTypes.Role, role),
            },
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Builds a <see cref="HubConnection"/> to the hub over the test server~ 🔌.</summary>
    /// <param name="token">Optional bearer token supplied via the access-token provider.</param>
    /// <param name="tokenInQueryString">When true, the token is placed in the URL query string instead.</param>
    /// <returns>An un-started hub connection.</returns>
    public HubConnection CreateHubConnection(string? token = null, bool tokenInQueryString = false)
    {
        var baseUri = this.Server.BaseAddress;
        var url = new Uri(baseUri, "/hubs/workflow").ToString();
        if (tokenInQueryString && token is not null)
        {
            url += "?access_token=" + Uri.EscapeDataString(token);
        }

        var builder = new HubConnectionBuilder().WithUrl(url, options =>
        {
            options.HttpMessageHandlerFactory = _ => this.Server.CreateHandler();
            options.Transports = HttpTransportType.LongPolling;
            if (token is not null && !tokenInQueryString)
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            }
        });

        return builder.Build();
    }

    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "sqlite",
                ["Persistence:ConnectionString"] = ":memory:",
                ["Api:Auth:Require"] = this.requireAuth ? "true" : "false",
                ["Api:Auth:Jwt:SigningKey"] = JwtSigningKey,
                ["Api:Auth:Jwt:Issuer"] = JwtIssuer,
                ["Api:Auth:Jwt:Audience"] = JwtAudience,
            };

            for (var i = 0; i < this.allowedOrigins.Length; i++)
            {
                settings[$"Api:RealTime:AllowedOrigins:{i}"] = this.allowedOrigins[i];
            }

            config.AddInMemoryCollection(settings);
        });
    }
}
