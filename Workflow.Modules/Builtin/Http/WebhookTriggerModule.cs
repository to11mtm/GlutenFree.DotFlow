// <copyright file="WebhookTriggerModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Http;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🪝 Phase 2.3.6 — Webhook trigger module (<c>builtin.http.webhook</c>)~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// This is a <b>trigger node</b>: the workflow is started by the webhook controller which
/// pre-populates the execution inputs with an <c>"__webhook__"</c> entry containing the
/// inbound HTTP request's body, headers, query params, method and received-at timestamp.
/// </para>
/// <para>
/// The module's <see cref="ExecuteAsync"/> simply unpacks that payload into named output ports
/// so downstream nodes can bind to <c>body</c>, <c>headers</c>, etc. without knowing about
/// the <c>__webhook__</c> convention~ 🌸
/// </para>
/// <para>
/// CopilotNote: When the <c>__webhook__</c> input is absent (e.g. in a non-webhook execution),
/// all outputs are <c>null</c>/empty and the module still succeeds. This keeps the module safe
/// to use in test harnesses that don't set up the full webhook infrastructure~ 🌸
/// </para>
/// </remarks>
public sealed class WebhookTriggerModule : IWorkflowModule
{
    /// <summary>Key used by the webhook controller to pre-populate trigger inputs~ 🔑.</summary>
    public const string WebhookInputKey = "__webhook__";

    /// <inheritdoc />
    public string ModuleId => "builtin.http.webhook";

    /// <inheritdoc />
    public string DisplayName => "Webhook Trigger";

    /// <inheritdoc />
    public string Category => "Triggers";

    /// <inheritdoc />
    public string Description => "Trigger node that fires when an inbound HTTP webhook is received~ 🪝✨";

    /// <inheritdoc />
    public string Icon => "🪝";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr<PortDefinition>.Empty,
        Outputs: Arr.create(
            new PortDefinition(
                Name: "body",
                DisplayName: "Request Body",
                DataType: typeof(object),
                Description: "Parsed inbound request body (object for JSON, string otherwise)~ 📦",
                IsRequired: false),
            new PortDefinition(
                Name: "headers",
                DisplayName: "Request Headers",
                DataType: typeof(Dictionary<string, string>),
                Description: "Flattened inbound request headers~ 🏷️",
                IsRequired: false),
            new PortDefinition(
                Name: "query",
                DisplayName: "Query Parameters",
                DataType: typeof(Dictionary<string, string>),
                Description: "Inbound URL query string parameters~ 🔍",
                IsRequired: false),
            new PortDefinition(
                Name: "method",
                DisplayName: "HTTP Method",
                DataType: typeof(string),
                Description: "HTTP method of the inbound request (e.g. POST, GET)~ 🏷️",
                IsRequired: false),
            new PortDefinition(
                Name: "receivedAt",
                DisplayName: "Received At",
                DataType: typeof(DateTimeOffset),
                Description: "UTC timestamp when the webhook was received by the API~ ⏰",
                IsRequired: false)),
        Properties: Arr.create(
            new ModulePropertyDefinition(
                Name: "webhookId",
                DisplayName: "Webhook ID",
                DataType: typeof(string),
                Description: "The registered webhook ID this trigger node is bound to (for documentation / IDE tooling)~ 🪝",
                IsRequired: true,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        if (!configuration.TryGetValue("webhookId", out var id)
            || id is null
            || string.IsNullOrWhiteSpace(id.ToString()))
        {
            return ValidationResult.Failure(new ValidationError(
                "REQUIRED_PROPERTY",
                "webhookId is required on the WebhookTriggerModule~ 💔",
                PropertyName: "webhookId"));
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // Try to unpack the __webhook__ payload that the controller injected~
        if (context.Inputs.TryGetValue(WebhookInputKey, out var raw)
            && raw is IDictionary<string, object?> payload)
        {
            var outputs = new Dictionary<string, object?>
            {
                ["body"]       = payload.TryGetValue("body", out var b) ? b : null,
                ["headers"]    = payload.TryGetValue("headers", out var h) ? h : new Dictionary<string, string>(),
                ["query"]      = payload.TryGetValue("query", out var q) ? q : new Dictionary<string, string>(),
                ["method"]     = payload.TryGetValue("method", out var m) ? m?.ToString() : null,
                ["receivedAt"] = payload.TryGetValue("receivedAt", out var r) ? r : null,
            };

            return Task.FromResult(ModuleResult.Ok(outputs));
        }

        // No webhook payload — outputs are empty (safe for non-webhook executions)~
        var empty = new Dictionary<string, object?>
        {
            ["body"]       = null,
            ["headers"]    = new Dictionary<string, string>(),
            ["query"]      = new Dictionary<string, string>(),
            ["method"]     = null,
            ["receivedAt"] = null,
        };

        return Task.FromResult(ModuleResult.Ok(empty));
    }
}


