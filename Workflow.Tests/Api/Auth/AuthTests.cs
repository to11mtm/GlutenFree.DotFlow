// <copyright file="AuthTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.Auth;

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Workflow.Api.Auth;
using Workflow.Tests.Api.V1;
using Xunit;

/// <summary>
/// 🔐 Phase 2.7.7 — API-key + JWT authentication and authorization-policy tests~ ✨.
/// </summary>
public sealed class AuthTests
{
    private const string ViewerKey = "viewer-secret-key";
    private const string DevKey = "developer-secret-key";
    private const string AdminKey = "admin-secret-key";
    private const string JwtSigningKey = "test-signing-key-that-is-at-least-32-bytes-long!!";
    private const string JwtIssuer = "dotflow-test";
    private const string JwtAudience = "dotflow-api";

    // ---------- API key ----------
    [Fact]
    public async Task ApiKey_ValidKey_Authenticates()
    {
        using var factory = new AuthFactory(require: true);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AuthConstants.ApiKeyHeader, ViewerKey);

        var resp = await client.GetAsync("/api/v1/modules");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ApiKey_InvalidKey_401()
    {
        using var factory = new AuthFactory(require: true);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AuthConstants.ApiKeyHeader, "totally-wrong");

        var resp = await client.GetAsync("/api/v1/modules");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ApiKey_MissingKey_AnonymousWhenNotRequired()
    {
        using var factory = new AuthFactory(require: false);
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/v1/modules");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ApiKey_MissingKey_401WhenRequired()
    {
        using var factory = new AuthFactory(require: true);
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/v1/modules");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ApiKey_CallerId_FlowsToExecutionAudit()
    {
        using var factory = new AuthFactory(require: true);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AuthConstants.ApiKeyHeader, DevKey);

        var create = await client.PostAsync("/api/v1/workflows", ExecutionEndpointsTests.PassthroughWorkflowJson("auth-" + Guid.NewGuid().ToString("N")));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var wfId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var run = await client.PostAsJsonAsync($"/api/v1/workflows/{wfId}/execute/sync?timeoutSeconds=30", new { inputs = new { } });
        var execId = (await run.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("executionId").GetGuid();

        var list = await client.GetFromJsonAsync<JsonElement>($"/api/v1/executions?workflowId={wfId}");
        var found = false;
        foreach (var item in list.GetProperty("items").EnumerateArray())
        {
            if (item.GetProperty("executionId").GetGuid() == execId)
            {
                item.GetProperty("triggeredBy").GetString().Should().Be("developer-user");
                found = true;
            }
        }

        found.Should().BeTrue();
    }

    // ---------- JWT ----------
    [Fact]
    public async Task Jwt_ValidToken_Authenticates()
    {
        using var factory = new AuthFactory(require: true);
        var client = factory.CreateClient();
        var token = MakeJwt(roles: AuthConstants.DeveloperRole, audience: JwtAudience, expires: DateTime.UtcNow.AddMinutes(10));
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/api/v1/modules");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Jwt_ExpiredToken_401()
    {
        using var factory = new AuthFactory(require: true);
        var client = factory.CreateClient();
        var token = MakeJwt(roles: AuthConstants.DeveloperRole, audience: JwtAudience, expires: DateTime.UtcNow.AddMinutes(-10));
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/api/v1/modules");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Jwt_WrongAudience_401()
    {
        using var factory = new AuthFactory(require: true);
        var client = factory.CreateClient();
        var token = MakeJwt(roles: AuthConstants.DeveloperRole, audience: "some-other-audience", expires: DateTime.UtcNow.AddMinutes(10));
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/api/v1/modules");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------- Policies ----------
    [Fact]
    public async Task Policy_WorkflowWrite_DeniesViewerRole_403()
    {
        using var factory = new AuthFactory(require: true);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AuthConstants.ApiKeyHeader, ViewerKey);

        var resp = await client.PostAsync("/api/v1/workflows", ExecutionEndpointsTests.PassthroughWorkflowJson("deny-" + Guid.NewGuid().ToString("N")));
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Policy_Admin_AllowsAdminRole()
    {
        using var factory = new AuthFactory(require: true);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AuthConstants.ApiKeyHeader, AdminKey);

        var create = await client.PostAsync("/api/v1/workflows", ExecutionEndpointsTests.PassthroughWorkflowJson("admin-" + Guid.NewGuid().ToString("N")));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var wfId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // DELETE is gated by the Admin policy~
        var del = await client.DeleteAsync($"/api/v1/workflows/{wfId}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AuthDisabled_Dev_AllEndpointsAnonymous()
    {
        using var factory = new AuthFactory(require: false);
        var client = factory.CreateClient();

        // No credentials at all — write endpoints are anonymous-friendly when auth is disabled~
        var resp = await client.PostAsync("/api/v1/workflows", ExecutionEndpointsTests.PassthroughWorkflowJson("anon-" + Guid.NewGuid().ToString("N")));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static string MakeJwt(string roles, string audience, DateTime expires)
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: audience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, "jwt-user"),
                new Claim(ClaimTypes.Role, roles),
            },
            notBefore: expires.AddMinutes(-30),
            expires: expires,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class AuthFactory : WebApplicationFactory<Program>
    {
        private readonly bool require;

        public AuthFactory(bool require)
        {
            this.require = require;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "sqlite",
                    ["Persistence:ConnectionString"] = ":memory:",
                    ["Api:Auth:Require"] = this.require ? "true" : "false",

                    ["Api:Auth:ApiKeys:0:KeyHash"] = ApiKeyHasher.Hash(ViewerKey),
                    ["Api:Auth:ApiKeys:0:CallerId"] = "viewer-user",
                    ["Api:Auth:ApiKeys:0:Roles:0"] = AuthConstants.ViewerRole,

                    ["Api:Auth:ApiKeys:1:KeyHash"] = ApiKeyHasher.Hash(DevKey),
                    ["Api:Auth:ApiKeys:1:CallerId"] = "developer-user",
                    ["Api:Auth:ApiKeys:1:Roles:0"] = AuthConstants.DeveloperRole,

                    ["Api:Auth:ApiKeys:2:KeyHash"] = ApiKeyHasher.Hash(AdminKey),
                    ["Api:Auth:ApiKeys:2:CallerId"] = "admin-user",
                    ["Api:Auth:ApiKeys:2:Roles:0"] = AuthConstants.AdminRole,

                    ["Api:Auth:Jwt:SigningKey"] = JwtSigningKey,
                    ["Api:Auth:Jwt:Issuer"] = JwtIssuer,
                    ["Api:Auth:Jwt:Audience"] = JwtAudience,
                });
            });
        }
    }
}
