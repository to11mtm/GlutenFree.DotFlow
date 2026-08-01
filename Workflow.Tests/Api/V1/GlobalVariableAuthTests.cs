// <copyright file="GlobalVariableAuthTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.V1;

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Workflow.Api.Auth;
using Xunit;

/// <summary>
/// 🔐 Phase 3.5 (V9.1) — global-scope variable writes require the Admin role, while
/// workflow/execution scope stays on WorkflowWrite.
/// </summary>
/// <remarks>
/// A global is environment configuration with blast radius across every workflow, so it shouldn't
/// share a permission with "can edit a workflow". This is a deliberate breaking change (plan Q14):
/// Developer-token automation managing globals now gets a 403.
/// </remarks>
public sealed class GlobalVariableAuthTests : IClassFixture<GlobalVariableAuthTests.AuthedApiFactory>
{
    private const string DevKey = "dev-key-v9";
    private const string AdminKey = "admin-key-v9";

    private readonly AuthedApiFactory factory;

    public GlobalVariableAuthTests(AuthedApiFactory factory) => this.factory = factory;

    private HttpClient ClientFor(string apiKey)
    {
        var client = this.factory.CreateClient();
        client.DefaultRequestHeaders.Add(AuthConstants.ApiKeyHeader, apiKey);
        return client;
    }

    private static JsonContent Body(object? value) => JsonContent.Create(new { value });

    [Fact]
    public async Task Developer_CannotWriteAGlobal()
    {
        var response = await this.ClientFor(DevKey)
            .PutAsync("/api/v1/variables/v9-dev-global?scope=global", Body("nope"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Admin");
    }

    [Fact]
    public async Task Developer_CannotDeleteAGlobal()
    {
        var response = await this.ClientFor(DevKey)
            .DeleteAsync("/api/v1/variables/v9-dev-delete?scope=global");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_CanWriteAGlobal()
    {
        var response = await this.ClientFor(AdminKey)
            .PutAsync("/api/v1/variables/v9-admin-global?scope=global", Body("yes"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Developer_CanStillWriteAWorkflowScopedVariable()
    {
        // Only the global scope tightened — per-workflow values are ordinary authoring.
        var workflowId = System.Guid.NewGuid();
        var response = await this.ClientFor(DevKey)
            .PutAsync($"/api/v1/variables/v9-wf?scope=workflow&scopeId={workflowId}", Body(1));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Developer_CanStillReadGlobals()
    {
        // Read stays on WorkflowRead — the designer's picker depends on it.
        var response = await this.ClientFor(DevKey).GetAsync("/api/v1/variables?scope=global");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    public sealed class AuthedApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "sqlite",
                    ["Persistence:ConnectionString"] = ":memory:",
                    ["Api:Auth:Require"] = "true",

                    ["Api:Auth:ApiKeys:0:KeyHash"] = ApiKeyHasher.Hash(DevKey),
                    ["Api:Auth:ApiKeys:0:CallerId"] = "developer-user",
                    ["Api:Auth:ApiKeys:0:Roles:0"] = AuthConstants.DeveloperRole,

                    ["Api:Auth:ApiKeys:1:KeyHash"] = ApiKeyHasher.Hash(AdminKey),
                    ["Api:Auth:ApiKeys:1:CallerId"] = "admin-user",
                    ["Api:Auth:ApiKeys:1:Roles:0"] = AuthConstants.AdminRole,
                });
            });
        }
    }
}
