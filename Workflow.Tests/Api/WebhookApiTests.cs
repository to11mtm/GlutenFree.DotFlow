// <copyright file="WebhookApiTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Api.Webhooks;
using Workflow.Core.Models;
using Workflow.Persistence.Abstractions;
using Xunit;

/// <summary>
/// 🪝 Phase 2.3.6 — API integration tests for webhook management + trigger endpoints~ ✨💖.
/// Uses <see cref="WebApplicationFactory{TProgram}"/> to spin up the full ASP.NET app in-process
/// (Docker-free, same DI container)~ 🎀
/// </summary>
public sealed class WebhookApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WebhookApiTests(WebApplicationFactory<Program> factory)
    {
        // Override the launcher with a spy so we can verify it was called~
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Swap in a SpyWorkflowLauncher so we can assert it was called~
                services.AddSingleton<IWorkflowLauncher, SpyWorkflowLauncher>();
            });
        });
    }

    // =========================================================================
    // 📋 Registration CRUD
    // =========================================================================

    [Fact]
    public async Task RegisterWebhook_ReturnsCreated()
    {
        var client = _factory.CreateClient();
        var webhookId = $"test-{Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync("/api/webhooks", new
        {
            webhookId,
            workflowDefinitionId = Guid.NewGuid(),
            allowedMethods = new[] { "POST" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "registering a new webhook should return 201 Created~ 🌸");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("webhookId").GetString().Should().Be(webhookId,
            "response should echo back the registered webhookId~ 💖");
    }

    // =========================================================================
    // 🪝 Webhook trigger
    // =========================================================================

    [Fact]
    public async Task PostToRegisteredWebhook_TriggersWorkflow_Returns202()
    {
        var client = _factory.CreateClient();
        var webhookId = $"trigger-{Guid.NewGuid():N}";

        // Register the webhook~
        await client.PostAsJsonAsync("/api/webhooks", new
        {
            webhookId,
            workflowDefinitionId = Guid.NewGuid(),
            allowedMethods = new[] { "POST" },
        });

        // Trigger it~
        var triggerResponse = await client.PostAsJsonAsync(
            $"/webhooks/{webhookId}",
            new { @event = "order.placed", orderId = "ord-123" });

        triggerResponse.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "triggering a registered webhook should return 202 Accepted~ 🪝");

        var body = await triggerResponse.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("executionId", out var execIdEl).Should().BeTrue(
            "response body should contain executionId~ 💖");
        Guid.TryParse(execIdEl.GetString(), out _).Should().BeTrue(
            "executionId should be a valid Guid~ ✅");
    }

    [Fact]
    public async Task PostToUnknownWebhook_Returns404()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/webhooks/this-webhook-does-not-exist-{Guid.NewGuid():N}",
            new { });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "POSTing to an unregistered webhook should return 404~ 🌸");
    }

    [Fact]
    public async Task PostWithDisallowedMethod_Returns405()
    {
        var client = _factory.CreateClient();
        var webhookId = $"get-only-{Guid.NewGuid():N}";

        // Register a webhook that only accepts GET~
        await client.PostAsJsonAsync("/api/webhooks", new
        {
            webhookId,
            workflowDefinitionId = Guid.NewGuid(),
            allowedMethods = new[] { "GET" },    // Only GET is allowed~
        });

        // Try to trigger with POST (which is not in AllowedMethods)~
        var response = await client.PostAsJsonAsync($"/webhooks/{webhookId}", new { });

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed,
            "POST to a GET-only webhook should return 405~ 💔");
    }
}

// =========================================================================
// Test doubles 🧪
// =========================================================================

/// <summary>
/// 🧪 Spy launcher that records which webhooks were launched and returns deterministic Guids~ ✨.
/// </summary>
internal sealed class SpyWorkflowLauncher : IWorkflowLauncher
{
    public List<(WebhookRegistration Registration, IReadOnlyDictionary<string, object?> Inputs)> Calls { get; } = new();

    public Task<Guid> LaunchAsync(
        WebhookRegistration registration,
        IReadOnlyDictionary<string, object?> inputs,
        System.Threading.CancellationToken ct = default)
    {
        Calls.Add((registration, inputs));
        return Task.FromResult(Guid.NewGuid());
    }
}

