// <copyright file="InMemoryPersistenceIsolationTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.V1;

using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

/// <summary>
/// The default persistence (<c>sqlite</c> + <c>:memory:</c>) gives each app instance its OWN
/// in-memory database. It used to normalize to a shared-cache <c>:memory:</c>, which is one
/// database per process: every default-config test host shared it, parallel hosts raced each
/// other's FluentMigrator runs ("SQLite Error 1: table VersionInfo already exists") and saw each
/// other's data.
/// </summary>
public sealed class InMemoryPersistenceIsolationTests
{
    [Fact]
    public async Task TwoDefaultHosts_DoNotShareTheInMemoryDatabase()
    {
        using var hostA = new WebApplicationFactory<Program>();
        using var hostB = new WebApplicationFactory<Program>();
        var clientA = hostA.CreateClient();
        var clientB = hostB.CreateClient();
        var name = "isolation-" + Guid.NewGuid().ToString("N");

        var created = await clientA.PostAsync("/api/v1/workflows", ExecutionEndpointsTests.PassthroughWorkflowJson(name));
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var seenByB = await clientB.GetFromJsonAsync<JsonElement>("/api/v1/workflows?pageSize=500");
        seenByB.ToString().Should().NotContain(name, because: "each host must get its own in-memory database");
    }

    [Fact]
    public async Task ManyDefaultHosts_StartingAtOnce_AllMigrateCleanly()
    {
        var hosts = Enumerable.Range(0, 8).Select(_ => new WebApplicationFactory<Program>()).ToList();
        try
        {
            // CreateClient starts the host (and runs migrations); do it concurrently~
            var starts = hosts.Select(h => Task.Run(() => h.CreateClient().GetAsync("/api/v1/health")));
            var responses = await Task.WhenAll(starts);

            responses.Should().OnlyContain(r => r.IsSuccessStatusCode);
        }
        finally
        {
            foreach (var host in hosts)
            {
                await host.DisposeAsync();
            }
        }
    }
}
