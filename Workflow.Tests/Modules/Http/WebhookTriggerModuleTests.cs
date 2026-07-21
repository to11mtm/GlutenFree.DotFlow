// <copyright file="WebhookTriggerModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Http;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Http;
using Workflow.Modules.Discovery;
using Workflow.Modules.Validation;
using Workflow.Persistence.Abstractions;
using Xunit;

/// <summary>
/// 🪝 Phase 2.3.6 — Tests for <see cref="WebhookTriggerModule"/> + <see cref="InMemoryWebhookRegistrationRepository"/>~ ✨💖.
/// </summary>
public sealed class WebhookTriggerModuleTests
{
    private readonly WebhookTriggerModule _module = new();

    #region Helpers 🛠️

    private static ModuleExecutionContext BuildContext(
        Dictionary<string, object?> inputs,
        Dictionary<string, object?>? properties = null)
        => new()
        {
            Inputs = inputs,
            Properties = properties ?? new Dictionary<string, object?> { ["webhookId"] = "test-hook" },
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new ServiceCollection().BuildServiceProvider(),
            ExecutionId = Guid.NewGuid(),
            NodeId = "trigger-node",
        };

    private static Dictionary<string, object?> WebhookPayload(
        object? body = null,
        string method = "POST",
        Dictionary<string, string>? headers = null,
        Dictionary<string, string>? query = null)
        => new()
        {
            ["body"] = body,
            ["headers"] = headers ?? new Dictionary<string, string>(),
            ["query"] = query ?? new Dictionary<string, string>(),
            ["method"] = method,
            ["receivedAt"] = DateTimeOffset.UtcNow,
        };

    #endregion

    // =========================================================================
    // 🏷️ Metadata + Schema
    // =========================================================================

    [Fact]
    public void WebhookTriggerModule_Metadata_IsCorrect()
    {
        _module.ModuleId.Should().Be("builtin.http.webhook");
        _module.DisplayName.Should().Be("Webhook Trigger");
        _module.Category.Should().Be("Triggers");
        _module.Icon.Should().Be("🪝");
        _module.Version.Should().Be(new Version(1, 0, 0));

        var validator = new ModuleValidator();
        validator.Validate(_module).IsValid.Should().BeTrue("module must pass ModuleValidator~ 💖");
    }

    [Fact]
    public void WebhookTriggerModule_Schema_HasCorrectPorts()
    {
        _module.Schema.Outputs.ToArray().Should().HaveCount(5, "body/headers/query/method/receivedAt~ 🌸");
        _module.Schema.Outputs.Select(p => p.Name).ToArray().Should()
            .Contain(new[] { "body", "headers", "query", "method", "receivedAt" });

        _module.Schema.Properties.ToArray().Should().ContainSingle(p => p.Name == "webhookId" && p.IsRequired);
    }

    // =========================================================================
    // 🪝 Execution
    // =========================================================================

    [Fact]
    public async Task WebhookTriggerModule_WithWebhookInputs_PopulatesOutputs()
    {
        var payload = WebhookPayload(
            body: new Dictionary<string, object?> { ["event"] = "user.created", ["id"] = "u-42" },
            method: "POST",
            headers: new Dictionary<string, string> { ["X-Source"] = "acme" },
            query: new Dictionary<string, string> { ["ref"] = "abc" });

        var ctx = BuildContext(inputs: new Dictionary<string, object?>
        {
            [WebhookTriggerModule.WebhookInputKey] = payload,
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Outputs["method"].Should().Be("POST");

        var headers = result.Outputs["headers"].Should().BeAssignableTo<IDictionary<string, string>>().Subject;
        headers["X-Source"].Should().Be("acme");

        var query = result.Outputs["query"].Should().BeAssignableTo<IDictionary<string, string>>().Subject;
        query["ref"].Should().Be("abc");

        var body = result.Outputs["body"].Should().BeAssignableTo<IDictionary<string, object?>>().Subject;
        body["event"].Should().Be("user.created");

        result.Outputs["receivedAt"].Should().NotBeNull("receivedAt should carry through~ 🌸");
    }

    [Fact]
    public async Task WebhookTriggerModule_WithoutWebhookInputs_OutputsAreEmpty()
    {
        // Simulates a workflow triggered WITHOUT the webhook controller — outputs still succeed~
        var ctx = BuildContext(inputs: new Dictionary<string, object?>());

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue("should not fail when no webhook payload present~ 🌸");
        result.Outputs["body"].Should().BeNull();
        result.Outputs["method"].Should().BeNull();
        result.Outputs["receivedAt"].Should().BeNull();
    }

    // =========================================================================
    // 📋 WebhookRegistration validation
    // =========================================================================

    [Fact]
    public void WebhookRegistration_ValidationRules()
    {
        // Valid registration~
        var valid = WebhookRegistration.Create("order-placed", Guid.NewGuid());
        valid.Validate().Should().BeEmpty("a well-formed registration should have no errors~ 💖");
        valid.AllowedMethods.ToArray().Should().Contain("POST", "default AllowedMethods should be POST~ 🌸");
        valid.Enabled.Should().BeTrue("default should be enabled~ ✅");

        // Empty WebhookId is invalid~
        var emptyId = new WebhookRegistration(
            WebhookId: "",
            WorkflowDefinitionId: Guid.NewGuid(),
            AllowedMethods: LanguageExt.Arr.create("POST"),
            SecretKey: LanguageExt.Option<string>.None,
            SignatureScheme: LanguageExt.Option<string>.None,
            CreatedAt: DateTimeOffset.UtcNow,
            Enabled: true);
        emptyId.Validate().Should().ContainSingle(e => e.Contains("WebhookId"),
            "empty WebhookId should produce a validation error~ 💔");

        // Guid.Empty WorkflowDefinitionId is invalid~
        var emptyWfId = WebhookRegistration.Create("hook", Guid.Empty);
        emptyWfId.Validate().Should().ContainSingle(e => e.Contains("WorkflowDefinitionId"),
            "Guid.Empty WorkflowDefinitionId should produce a validation error~ 💔");
    }

    // =========================================================================
    // 💾 InMemoryWebhookRegistrationRepository
    // =========================================================================

    [Fact]
    public async Task InMemoryRepository_RegisterAndGet_RoundTrips()
    {
        var repo = new InMemoryWebhookRegistrationRepository();
        var reg = WebhookRegistration.Create("my-hook", Guid.NewGuid());

        var result = await repo.RegisterAsync(reg);
        result.Success.Should().BeTrue();
        result.Registration.Should().NotBeNull();

        var fetched = await repo.GetAsync("my-hook");
        fetched.Should().NotBeNull();
        fetched!.WebhookId.Should().Be("my-hook");
        fetched.WorkflowDefinitionId.Should().Be(reg.WorkflowDefinitionId);
    }

    [Fact]
    public async Task InMemoryRepository_DuplicateId_ReturnsConflictError()
    {
        var repo = new InMemoryWebhookRegistrationRepository();
        var reg = WebhookRegistration.Create("duplicate-hook", Guid.NewGuid());

        var first = await repo.RegisterAsync(reg);
        first.Success.Should().BeTrue();

        var second = await repo.RegisterAsync(reg);
        second.Success.Should().BeFalse("second register with same ID should conflict~ 💔");
        second.ErrorCode.Should().Be("CONFLICT");
    }

    [Fact]
    public async Task InMemoryRepository_Delete_RemovesEntry()
    {
        var repo = new InMemoryWebhookRegistrationRepository();
        var reg = WebhookRegistration.Create("delete-me", Guid.NewGuid());
        await repo.RegisterAsync(reg);

        var deleted = await repo.DeleteAsync("delete-me");
        deleted.Should().BeTrue("should report it was deleted~ 🗑️");

        var fetched = await repo.GetAsync("delete-me");
        fetched.Should().BeNull("deleted webhook should not be found~ 🌸");
    }
}



