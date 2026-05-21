// <copyright file="WebhookDispatcher.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Webhooks;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Workflow.Persistence.Abstractions;

/// <summary>
/// 🪝 Phase 2.3.6 — Encapsulates webhook lookup, method validation, payload assembly, workflow
/// launch, and HTTP response via <see cref="IWebhookResponseStrategy"/>~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// Intentionally isolated from routing so a future arbitrary-path router (2.3.P1) can sit
/// in front without touching this class. The method signature is:
/// <c>DispatchAsync(webhookId, context, responseStrategy, ct)</c>
/// — callers supply the resolved strategy so advanced hosts can vary it per-request~ 🧠
/// </para>
/// <para>
/// CopilotNote: The <c>__webhook__</c> input convention must be kept stable — the
/// <c>builtin.http.webhook</c> trigger module depends on it~ 🌸
/// </para>
/// </remarks>
public sealed class WebhookDispatcher
{
    private readonly IWebhookRegistrationRepository _repo;
    private readonly IWorkflowLauncher _launcher;
    private readonly ILogger<WebhookDispatcher> _logger;

    /// <summary>Initialises the dispatcher with all injected dependencies~ 💉.</summary>
    public WebhookDispatcher(
        IWebhookRegistrationRepository repo,
        IWorkflowLauncher launcher,
        ILogger<WebhookDispatcher> logger)
    {
        _repo = repo;
        _launcher = launcher;
        _logger = logger;
    }

    /// <summary>
    /// Handle a webhook trigger request for <paramref name="webhookId"/>.
    /// Writes the HTTP response before returning~ 🌐.
    /// </summary>
    /// <param name="webhookId">The webhook slug from the URL.</param>
    /// <param name="context">Current HTTP context.</param>
    /// <param name="responseStrategy">Strategy that writes the final HTTP response (e.g. 202 Accepted).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task DispatchAsync(
        string webhookId,
        HttpContext context,
        IWebhookResponseStrategy responseStrategy,
        CancellationToken ct = default)
    {
        // 1️⃣ Look up registration~
        var registration = await _repo.GetAsync(webhookId, ct).ConfigureAwait(false);

        if (registration is null || !registration.Enabled)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(
                new { error = $"Webhook '{webhookId}' not found." }, ct).ConfigureAwait(false);
            return;
        }

        // 2️⃣ Check allowed HTTP methods~
        var incomingMethod = context.Request.Method.ToUpperInvariant();
        if (registration.AllowedMethods.Count > 0
            && !registration.AllowedMethods.Any(m =>
                string.Equals(m, incomingMethod, StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            context.Response.Headers.Allow = string.Join(", ", registration.AllowedMethods);
            await context.Response.WriteAsJsonAsync(
                new
                {
                    error = $"Method {incomingMethod} is not allowed for webhook '{webhookId}'.",
                    allowed = registration.AllowedMethods.ToArray(),
                },
                ct).ConfigureAwait(false);
            return;
        }

        // 3️⃣ Read + parse the request body~
        object? body = null;
        try
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            var rawBody = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            context.Request.Body.Position = 0;

            if (!string.IsNullOrEmpty(rawBody))
            {
                try
                {
                    using var doc = JsonDocument.Parse(rawBody);
                    body = JsonElementToObject(doc.RootElement);
                }
                catch (JsonException)
                {
                    // Non-JSON body — pass as raw string~
                    body = rawBody;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("🪝 Could not read request body for webhook '{WebhookId}': {Error}~", webhookId, ex.Message);
        }

        // 4️⃣ Build the __webhook__ input payload~
        var flatHeaders = context.Request.Headers
            .ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var flatQuery = context.Request.Query
            .ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        var webhookPayload = new Dictionary<string, object?>
        {
            ["body"] = body,
            ["headers"] = flatHeaders,
            ["query"] = flatQuery,
            ["method"] = context.Request.Method,
            ["receivedAt"] = DateTimeOffset.UtcNow,
        };

        var inputs = new Dictionary<string, object?> { ["__webhook__"] = webhookPayload };

        // 5️⃣ Launch the workflow~
        var executionId = await _launcher.LaunchAsync(registration, inputs, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "🪝 Webhook '{WebhookId}' triggered → execution {ExecutionId} for workflow {WorkflowId}~",
            webhookId, executionId, registration.WorkflowDefinitionId);

        // 6️⃣ Delegate to response strategy (V1 default = 202 Accepted)~
        await responseStrategy.RespondAsync(context, executionId, ct).ConfigureAwait(false);
    }

    /// <summary>Recursively convert a <see cref="JsonElement"/> to POCO primitives/dicts/lists~ 🔧.</summary>
    private static object? JsonElementToObject(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.Object => el.EnumerateObject()
                .ToDictionary(p => p.Name, p => JsonElementToObject(p.Value)),
            JsonValueKind.Array => el.EnumerateArray()
                .Select(JsonElementToObject).ToList<object?>(),
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? (object?)l : el.GetDouble(),
            JsonValueKind.True => (object?)true,
            JsonValueKind.False => (object?)false,
            _ => (object?)null,
        };
    }
}

