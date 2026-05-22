// <copyright file="WebhookEndpoints.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Webhooks;

using System;
using System.Linq;
using LanguageExt;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Workflow.Core.Models;
using Workflow.Persistence.Abstractions;

/// <summary>
/// 🪝 Phase 2.3.6 — Minimal-API route mappings for webhook trigger + management endpoints~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// Call <see cref="MapWebhookEndpoints"/> on your <see cref="IEndpointRouteBuilder"/> at app startup.
/// </para>
/// <para>
/// Routes:
/// <list type="bullet">
///   <item><description><c>ANY /webhooks/{webhookId}</c> — trigger (forward-compat for 2.3.P1)</description></item>
///   <item><description><c>POST /api/webhooks</c> — register</description></item>
///   <item><description><c>GET /api/webhooks</c> — list</description></item>
///   <item><description><c>GET /api/webhooks/{webhookId}</c> — get one</description></item>
///   <item><description><c>PUT /api/webhooks/{webhookId}</c> — update</description></item>
///   <item><description><c>DELETE /api/webhooks/{webhookId}</c> — delete</description></item>
/// </list>
/// </para>
/// <para>
/// CopilotNote: The trigger route is mapped with all common HTTP methods (not just POST) so
/// <c>AllowedMethods</c> validation happens INSIDE the dispatcher, and future arbitrary-path
/// routing (2.3.P1) can call DispatchAsync without changing the route shape~ 🧠
/// </para>
/// </remarks>
public static class WebhookEndpoints
{
    // =========================================================================
    // Route extension method 🗺️
    // =========================================================================

    /// <summary>
    /// Register all webhook-related endpoints on <paramref name="app"/>~ 🪝.
    /// </summary>
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        // --- Trigger route (any HTTP method — AllowedMethods checked inside dispatcher) ---
        app.MapMethods(
            "/webhooks/{webhookId}",
            new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" },
            async (string webhookId, HttpContext ctx, WebhookDispatcher dispatcher, IWebhookResponseStrategy strategy, CancellationToken ct) =>
            {
                await dispatcher.DispatchAsync(webhookId, ctx, strategy, ct).ConfigureAwait(false);
            })
            .WithName("TriggerWebhook")
            .WithTags("Webhooks")
            .WithSummary("Trigger a registered webhook")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status405MethodNotAllowed);

        // --- Management routes ---
        var group = app.MapGroup("/api/webhooks").WithTags("Webhook Management");

        group.MapPost("/", RegisterWebhookHandler)
            .WithName("RegisterWebhook")
            .WithSummary("Register a new webhook")
            .Produces<WebhookRegistration>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict);

        group.MapGet("/", ListWebhooksHandler)
            .WithName("ListWebhooks")
            .WithSummary("List all registered webhooks")
            .Produces<WebhookRegistration[]>(StatusCodes.Status200OK);

        group.MapGet("/{webhookId}", GetWebhookHandler)
            .WithName("GetWebhook")
            .WithSummary("Get a webhook by ID")
            .Produces<WebhookRegistration>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/{webhookId}", UpdateWebhookHandler)
            .WithName("UpdateWebhook")
            .WithSummary("Update an existing webhook")
            .Produces<WebhookRegistration>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{webhookId}", DeleteWebhookHandler)
            .WithName("DeleteWebhook")
            .WithSummary("Delete a webhook by ID")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    // =========================================================================
    // Handlers 🛠️
    // =========================================================================

    private static async Task<IResult> RegisterWebhookHandler(
        RegisterWebhookRequest req,
        IWebhookRegistrationRepository repo,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.WebhookId))
        {
            return Results.BadRequest(new { error = "WebhookId is required." });
        }

        if (req.WorkflowDefinitionId == Guid.Empty)
        {
            return Results.BadRequest(new { error = "WorkflowDefinitionId must not be Guid.Empty." });
        }

        // 🔒 Phase 2.3.7 — Validate signature scheme at registration time~
        if (req.SignatureScheme is { Length: > 0 })
        {
            if (string.IsNullOrWhiteSpace(req.SecretKey))
            {
                return Results.BadRequest(new { error = "SecretKey is required when SignatureScheme is specified." });
            }

            if (!WebhookSignatureValidatorRegistry.IsKnownScheme(req.SignatureScheme))
            {
                return Results.BadRequest(new
                {
                    error = $"Unknown signature scheme '{req.SignatureScheme}'. Known schemes: hmac-sha256, github, stripe.",
                });
            }
        }

        var registration = WebhookRegistration.Create(
            req.WebhookId,
            req.WorkflowDefinitionId,
            req.AllowedMethods) with
        {
            Enabled = req.Enabled ?? true,
            SecretKey = req.SecretKey is { Length: > 0 } sk
                ? Option<string>.Some(sk)
                : Option<string>.None,
            SignatureScheme = req.SignatureScheme is { Length: > 0 } ss
                ? Option<string>.Some(ss)
                : Option<string>.None,
        };

        var result = await repo.RegisterAsync(registration, ct).ConfigureAwait(false);

        return result.Success
            ? Results.Created($"/api/webhooks/{registration.WebhookId}", result.Registration)
            : Results.Conflict(new { error = result.Error });
    }

    private static async Task<IResult> ListWebhooksHandler(
        IWebhookRegistrationRepository repo,
        CancellationToken ct)
    {
        var list = await repo.ListAsync(ct).ConfigureAwait(false);
        return Results.Ok(list);
    }

    private static async Task<IResult> GetWebhookHandler(
        string webhookId,
        IWebhookRegistrationRepository repo,
        CancellationToken ct)
    {
        var registration = await repo.GetAsync(webhookId, ct).ConfigureAwait(false);
        return registration is null
            ? Results.NotFound(new { error = $"Webhook '{webhookId}' not found." })
            : Results.Ok(registration);
    }

    private static async Task<IResult> UpdateWebhookHandler(
        string webhookId,
        RegisterWebhookRequest req,
        IWebhookRegistrationRepository repo,
        CancellationToken ct)
    {
        var existing = await repo.GetAsync(webhookId, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return Results.NotFound(new { error = $"Webhook '{webhookId}' not found." });
        }

        // 🔒 Phase 2.3.7 — Validate signature scheme on update too~
        if (req.SignatureScheme is { Length: > 0 })
        {
            if (string.IsNullOrWhiteSpace(req.SecretKey))
            {
                return Results.BadRequest(new { error = "SecretKey is required when SignatureScheme is specified." });
            }

            if (!WebhookSignatureValidatorRegistry.IsKnownScheme(req.SignatureScheme))
            {
                return Results.BadRequest(new
                {
                    error = $"Unknown signature scheme '{req.SignatureScheme}'. Known schemes: hmac-sha256, github, stripe.",
                });
            }
        }

        var updated = WebhookRegistration.Create(
            webhookId,
            req.WorkflowDefinitionId == Guid.Empty ? existing.WorkflowDefinitionId : req.WorkflowDefinitionId,
            req.AllowedMethods ?? existing.AllowedMethods.ToArray()) with
        {
            Enabled = req.Enabled ?? existing.Enabled,
            CreatedAt = existing.CreatedAt,
            SecretKey = req.SecretKey is { Length: > 0 } sk
                ? Option<string>.Some(sk)
                : existing.SecretKey,
            SignatureScheme = req.SignatureScheme is { Length: > 0 } ss
                ? Option<string>.Some(ss)
                : existing.SignatureScheme,
        };

        var result = await repo.UpdateAsync(updated, ct).ConfigureAwait(false);
        return result.Success
            ? Results.Ok(result.Registration)
            : Results.NotFound(new { error = result.Error });
    }

    private static async Task<IResult> DeleteWebhookHandler(
        string webhookId,
        IWebhookRegistrationRepository repo,
        CancellationToken ct)
    {
        var deleted = await repo.DeleteAsync(webhookId, ct).ConfigureAwait(false);
        return deleted
            ? Results.NoContent()
            : Results.NotFound(new { error = $"Webhook '{webhookId}' not found." });
    }
}

// =========================================================================
// DTOs 📦
// =========================================================================

/// <summary>Request body for creating or updating a webhook~ 📋.</summary>
/// <param name="WebhookId">Stable URL slug for the webhook.</param>
/// <param name="WorkflowDefinitionId">Workflow to run when the webhook fires.</param>
/// <param name="AllowedMethods">Optional HTTP methods (default <c>["POST"]</c>).</param>
/// <param name="Enabled">Whether the webhook is active (default <c>true</c>).</param>
/// <param name="SecretKey">
/// Optional HMAC secret — required when <paramref name="SignatureScheme"/> is set~ 🔒.
/// </param>
/// <param name="SignatureScheme">
/// Optional signature scheme: <c>"hmac-sha256"</c>, <c>"github"</c>, or <c>"stripe"</c>~ 🔐.
/// When set, trigger requests that fail signature validation are rejected with 401 Unauthorized.
/// </param>
public sealed record RegisterWebhookRequest(
    string WebhookId,
    Guid WorkflowDefinitionId,
    string[]? AllowedMethods = null,
    bool? Enabled = null,
    string? SecretKey = null,
    string? SignatureScheme = null);

