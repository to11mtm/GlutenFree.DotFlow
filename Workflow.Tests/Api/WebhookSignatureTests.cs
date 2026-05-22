// <copyright file="WebhookSignatureTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api;

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Api.Webhooks;
using Xunit;

/// <summary>
/// 🔒 Phase 2.3.7 — API integration tests for webhook signature validation~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// Each test:
///   1. Registers a webhook with a <c>secretKey</c> and <c>signatureScheme</c>~ 📋
///   2. Crafts the correct (or deliberately wrong) signature header~ 🔐
///   3. POSTs to the trigger endpoint and asserts the expected HTTP status~
/// </para>
/// <para>
/// CopilotNote: Signature computation in tests mirrors the exact algorithm in each
/// <see cref="IWebhookSignatureValidator"/> implementation — if the implementations change,
/// the test helper methods must change too~ 🧠
/// </para>
/// </remarks>
public sealed class WebhookSignatureTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestSecret = "super-secret-key-uwu-🔒";
    private readonly WebApplicationFactory<Program> _factory;

    public WebhookSignatureTests(WebApplicationFactory<Program> factory)
    {
        // Override the launcher with a no-op so tests don't need the Akka runtime~
        _factory = factory.WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddSingleton<IWorkflowLauncher, SigTestLauncher>()));
    }

    // =========================================================================
    // 🔐 Generic HMAC-SHA256 (scheme: "hmac-sha256", header: X-Signature)
    // =========================================================================

    [Fact]
    public async Task HmacSha256_ValidSignature_Passes()
    {
        var client = _factory.CreateClient();
        var webhookId = await RegisterAsync(client, "hmac-sha256");

        var body = """{"event":"order.placed"}""";
        var sig = ComputeHmacHex(TestSecret, body);

        var response = await TriggerAsync(client, webhookId, body,
            ("X-Signature", sig));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "a correctly-signed request should trigger the workflow and return 202~ 🪝✅");
    }

    [Fact]
    public async Task HmacSha256_InvalidSignature_Rejected()
    {
        var client = _factory.CreateClient();
        var webhookId = await RegisterAsync(client, "hmac-sha256");

        var body = """{"event":"order.placed"}""";

        // Deliberately wrong signature — all zeroes~
        var response = await TriggerAsync(client, webhookId, body,
            ("X-Signature", new string('0', 64)));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a request with a wrong signature should be rejected with 401~ 🚫🔒");
    }

    [Fact]
    public async Task HmacSha256_MissingHeader_Rejected()
    {
        var client = _factory.CreateClient();
        var webhookId = await RegisterAsync(client, "hmac-sha256");

        // No X-Signature header at all~
        var response = await TriggerAsync(client, webhookId, """{"event":"order.placed"}""");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a request with no signature header should be rejected with 401~ 🚫🔒");
    }

    // =========================================================================
    // 🐙 GitHub (scheme: "github", header: X-Hub-Signature-256: sha256={hex})
    // =========================================================================

    [Fact]
    public async Task GitHub_ValidSignature_Passes()
    {
        var client = _factory.CreateClient();
        var webhookId = await RegisterAsync(client, "github");

        var body = """{"action":"push","ref":"refs/heads/main"}""";
        var hex = ComputeHmacHex(TestSecret, body);

        var response = await TriggerAsync(client, webhookId, body,
            ("X-Hub-Signature-256", $"sha256={hex}"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "a GitHub-signed request with the correct sha256= header should return 202~ 🐙✅");
    }

    // =========================================================================
    // 💳 Stripe (scheme: "stripe", header: Stripe-Signature: t={unix},v1={hex})
    // =========================================================================

    [Fact]
    public async Task Stripe_ValidSignatureWithinTolerance_Passes()
    {
        var client = _factory.CreateClient();
        var webhookId = await RegisterAsync(client, "stripe");

        var body = """{"type":"payment_intent.succeeded","id":"pi_123"}""";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig = ComputeStripeSignature(TestSecret, timestamp, body);

        var response = await TriggerAsync(client, webhookId, body,
            ("Stripe-Signature", $"t={timestamp},v1={sig}"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "a Stripe-signed request with a fresh timestamp should return 202~ 💳✅");
    }

    [Fact]
    public async Task Stripe_ExpiredTimestamp_Rejected()
    {
        var client = _factory.CreateClient();
        var webhookId = await RegisterAsync(client, "stripe");

        var body = """{"type":"charge.refunded","id":"ch_456"}""";

        // 10 minutes ago — outside the default 5-minute tolerance window~ ⏰
        var expiredTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        var sig = ComputeStripeSignature(TestSecret, expiredTimestamp, body);

        var response = await TriggerAsync(client, webhookId, body,
            ("Stripe-Signature", $"t={expiredTimestamp},v1={sig}"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a Stripe request older than the 5-minute tolerance should be rejected as a possible replay~ 🛡️⏰");
    }

    // =========================================================================
    // ❓ Unknown scheme — rejected at registration time
    // =========================================================================

    [Fact]
    public async Task UnknownScheme_RejectedAtRegistration()
    {
        var client = _factory.CreateClient();
        var webhookId = $"sig-{Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync("/api/webhooks", new
        {
            webhookId,
            workflowDefinitionId = Guid.NewGuid(),
            allowedMethods = new[] { "POST" },
            secretKey = TestSecret,
            signatureScheme = "custom-unknown-scheme",   // ← not registered in the registry~
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "registering a webhook with an unknown signatureScheme should return 400~ 🚫");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should()
            .Contain("Unknown signature scheme", "the error message should name the bad scheme~ 💔");
    }

    // =========================================================================
    // Helpers 🛠️
    // =========================================================================

    /// <summary>Register a webhook with the given signature scheme and return its ID~ 📋.</summary>
    private static async Task<string> RegisterAsync(
        HttpClient client,
        string signatureScheme)
    {
        var webhookId = $"sig-{signatureScheme}-{Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync("/api/webhooks", new
        {
            webhookId,
            workflowDefinitionId = Guid.NewGuid(),
            allowedMethods = new[] { "POST" },
            secretKey = TestSecret,
            signatureScheme,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            $"registering a webhook with scheme '{signatureScheme}' should succeed~ 🌸");

        return webhookId;
    }

    /// <summary>
    /// POST to <c>/webhooks/{webhookId}</c> with <paramref name="body"/> and optional extra
    /// headers~ 🌐.
    /// </summary>
    private static async Task<HttpResponseMessage> TriggerAsync(
        HttpClient client,
        string webhookId,
        string body,
        params (string Name, string Value)[] extraHeaders)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/webhooks/{webhookId}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        foreach (var (name, value) in extraHeaders)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return await client.SendAsync(request);
    }

    /// <summary>
    /// Compute HMAC-SHA256 over <paramref name="bodyUtf8"/> with <paramref name="secret"/>
    /// and return lowercase hex~ 🔐.
    /// </summary>
    private static string ComputeHmacHex(string secret, string bodyUtf8)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var bodyBytes = Encoding.UTF8.GetBytes(bodyUtf8);
        using var hmac = new HMACSHA256(keyBytes);
        return Convert.ToHexString(hmac.ComputeHash(bodyBytes)).ToLowerInvariant();
    }

    /// <summary>
    /// Compute the Stripe v1 signature: HMAC-SHA256 of <c>"{timestamp}.{body}"</c>~ 💳.
    /// </summary>
    private static string ComputeStripeSignature(string secret, long timestamp, string bodyUtf8)
    {
        var signedPayload = Encoding.UTF8.GetBytes($"{timestamp}.{bodyUtf8}");
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        using var hmac = new HMACSHA256(keyBytes);
        return Convert.ToHexString(hmac.ComputeHash(signedPayload)).ToLowerInvariant();
    }
}

// =============================================================================
// Test doubles 🧪
// =============================================================================

/// <summary>
/// 🧪 No-op launcher for signature tests — we only care about the HTTP status code,
/// not what happens after the workflow is triggered~ ✨.
/// </summary>
internal sealed class SigTestLauncher : IWorkflowLauncher
{
    public Task<Guid> LaunchAsync(
        global::Workflow.Core.Models.WebhookRegistration registration,
        System.Collections.Generic.IReadOnlyDictionary<string, object?> inputs,
        System.Threading.CancellationToken ct = default)
        => Task.FromResult(Guid.NewGuid());
}

