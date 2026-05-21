// <copyright file="WebhookRegistration.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Core.Models;

using System;
using System.Collections.Generic;
using LanguageExt;

/// <summary>
/// 🪝 Phase 2.3.6 — Describes a registered webhook endpoint that can trigger a workflow execution~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="WebhookRegistration"/> binds a stable webhook URL slug (<see cref="WebhookId"/>)
/// to a <see cref="WorkflowDefinitionId"/> and declares which HTTP methods the endpoint accepts.
/// Optional <see cref="SecretKey"/> + <see cref="SignatureScheme"/> fields are reserved for
/// Phase 2.3.7 HMAC signature validation~ 🔒
/// </para>
/// <para>
/// CopilotNote: <c>AllowedMethods</c> are normalised to UPPER-CASE on creation via
/// <see cref="Create"/>. Always compare against the registry using
/// <see cref="StringComparer.OrdinalIgnoreCase"/> for safety~ 🧠
/// </para>
/// </remarks>
/// <param name="WebhookId">Stable URL slug (e.g. <c>"order-placed"</c>). Case-insensitive on lookup.</param>
/// <param name="WorkflowDefinitionId">ID of the workflow to start when this webhook fires.</param>
/// <param name="AllowedMethods">HTTP methods that are permitted (e.g. <c>["POST"]</c>).</param>
/// <param name="SecretKey">Optional HMAC secret (used in 2.3.7 signature validation).</param>
/// <param name="SignatureScheme">Optional scheme name: <c>"hmac-sha256"</c>, <c>"github"</c>, <c>"stripe"</c>.</param>
/// <param name="CreatedAt">UTC timestamp of registration.</param>
/// <param name="Enabled">When <c>false</c>, the trigger endpoint returns 404 as if unregistered.</param>
public sealed record WebhookRegistration(
    string WebhookId,
    Guid WorkflowDefinitionId,
    Arr<string> AllowedMethods,
    Option<string> SecretKey,
    Option<string> SignatureScheme,
    DateTimeOffset CreatedAt,
    bool Enabled)
{
    /// <summary>
    /// Factory helper — creates a minimal registration with sensible defaults (POST only, enabled)~ 🏭.
    /// </summary>
    /// <param name="webhookId">Stable URL slug.</param>
    /// <param name="workflowDefinitionId">Workflow to run when triggered.</param>
    /// <param name="allowedMethods">
    /// Optional list of allowed HTTP methods (default <c>["POST"]</c>). All values are upper-cased.
    /// </param>
    public static WebhookRegistration Create(
        string webhookId,
        Guid workflowDefinitionId,
        IEnumerable<string>? allowedMethods = null)
    {
        var methods = (allowedMethods ?? new[] { "POST" })
            .Select(m => m.Trim().ToUpperInvariant())
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct()
            .ToArray();

        return new WebhookRegistration(
            WebhookId: webhookId,
            WorkflowDefinitionId: workflowDefinitionId,
            AllowedMethods: Arr.create(methods),
            SecretKey: Option<string>.None,
            SignatureScheme: Option<string>.None,
            CreatedAt: DateTimeOffset.UtcNow,
            Enabled: true);
    }

    /// <summary>
    /// Validates the registration and returns a list of error messages (empty = valid)~ ✅.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(WebhookId))
        {
            errors.Add("WebhookId is required and cannot be blank.");
        }

        if (WorkflowDefinitionId == Guid.Empty)
        {
            errors.Add("WorkflowDefinitionId must not be Guid.Empty.");
        }

        if (AllowedMethods.Count == 0)
        {
            errors.Add("AllowedMethods must contain at least one HTTP method.");
        }

        return errors;
    }
}

