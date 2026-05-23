// <copyright file="WebhookApiActorTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using LanguageExt;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Api.Webhooks;
using Workflow.Core.Models;
using Workflow.Engine.Messages;
using Workflow.Persistence.Abstractions;
using Xunit;

/// <summary>
///  Phase 2.3.9 — Tests for <see cref="ActorWorkflowLauncher"/> + the webhook API
/// path when a real launcher is in place~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// Three scenarios:
/// <list type="bullet">
///   <item>Unit test: <see cref="ActorWorkflowLauncher"/> sends correct message to supervisor~ ✅</item>
///   <item>API test: launcher returns a real execution ID → 202 Accepted~ </item>
///   <item>API test: launcher throws <see cref="WorkflowLaunchException"/> → 500~ </item>
/// </list>
/// </para>
/// <para>
/// CopilotNote: The TestKit-based test spins up an Akka ActorSystem (via TestKit).
/// The WebApplicationFactory tests override <c>IWorkflowLauncher</c> with test doubles to keep
/// the API tests Docker-free and deterministic~
/// </para>
/// </remarks>
public sealed class WebhookApiActorTests : TestKit
{
    // =========================================================================
    //  Test 1 — ActorWorkflowLauncher sends correct message to supervisor
    // =========================================================================

    /// <summary>
    /// <see cref="ActorWorkflowLauncher.LaunchAsync"/> should send a
    /// <see cref="CreateWorkflowInstance"/> to the supervisor and return the execution ID
    /// from the <see cref="WorkflowInstanceCreated"/> reply~ ✅.
    /// </summary>
    [Fact]
    public async Task ActorWorkflowLauncher_Launch_CreatesExecution()
    {
        // Arrange — use a TestProbe as the fake supervisor~
        var probe = CreateTestProbe();
        var supervisorRef = new WorkflowSupervisorActorRef(probe.Ref);

        var workflowId = Guid.NewGuid();
        var definition = BuildMinimalDefinition(workflowId);
        var fakeRepo = new StubWorkflowRepository(definition);

        var launcher = new ActorWorkflowLauncher(
            supervisorRef,
            fakeRepo,
            NullLogger<ActorWorkflowLauncher>.Instance);

        var registration = WebhookRegistration.Create("test-hook", workflowId);
        var inputs = new Dictionary<string, object?> { ["__webhook__"] = "payload" };
        var expectedExecutionId = Guid.NewGuid();

        // Arrange — the probe will respond with WorkflowInstanceCreated~
        var launchTask = launcher.LaunchAsync(registration, inputs, CancellationToken.None);

        // The probe should receive CreateWorkflowInstance~
        var msg = probe.ExpectMsg<CreateWorkflowInstance>(TimeSpan.FromSeconds(5));
        msg.WorkflowId.Should().Be(workflowId,
            "WorkflowId should match the registration's WorkflowDefinitionId~");
        msg.Inputs.ContainsKey("__webhook__").Should().BeTrue(
            "webhook payload should be in the inputs~");

        // Reply with WorkflowInstanceCreated~
        probe.Reply(new WorkflowInstanceCreated(expectedExecutionId, workflowId));

        // Act + Assert~
        var executionId = await launchTask;
        executionId.Should().Be(expectedExecutionId,
            "LaunchAsync should return the execution ID from the supervisor reply~ ");
    }

    // =========================================================================
    //  Test 2 — API returns real executionId when ActorWorkflowLauncher succeeds
    // =========================================================================

    /// <summary>
    /// POST to a registered webhook with <see cref="ActorWorkflowLauncher"/> active
    /// should return 202 Accepted with a real <c>executionId</c>~ .
    /// </summary>
    [Fact]
    public async Task Api_PostToWebhook_ActorLauncher_ReturnsRealExecutionId()
    {
        var webhookId = $"actor-test-{Guid.NewGuid():N}";
        var workflowId = Guid.NewGuid();
        var executionId = Guid.NewGuid();

        // Use a preset ExecutionActorLauncher with a known execution ID~
        var launcher = new FixedSuccessLauncher(executionId);

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(services =>
            {
                services.AddSingleton<IWorkflowLauncher>(launcher);
            }));

        var client = factory.CreateClient();

        // Register the webhook~
        await client.PostAsJsonAsync("/api/webhooks", new
        {
            webhookId,
            workflowDefinitionId = workflowId,
            allowedMethods = new[] { "POST" },
        });

        // Trigger it~
        var response = await client.PostAsJsonAsync(
            $"/webhooks/{webhookId}",
            new { @event = "order.created" });

        // Assert~
        response.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "ActorWorkflowLauncher returning a valid execution ID should yield 202~ ✅");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("executionId", out var execEl).Should().BeTrue(
            "response should contain executionId~");
        Guid.Parse(execEl.GetString()!).Should().Be(executionId,
            "the returned executionId should match what the launcher returned~ ");
    }

    // =========================================================================
    //  Test 3 — API returns 500 when launcher throws WorkflowLaunchException
    // =========================================================================

    /// <summary>
    /// When <see cref="ActorWorkflowLauncher.LaunchAsync"/> throws
    /// <see cref="WorkflowLaunchException"/>, the dispatcher should swallow it and
    /// return <c>500 Internal Server Error</c> (no raw exception leaked to caller)~ .
    /// </summary>
    [Fact]
    public async Task Api_PostToWebhook_UnknownDefinition_Returns500()
    {
        var webhookId = $"fail-test-{Guid.NewGuid():N}";
        var workflowId = Guid.NewGuid();

        // Use a launcher that throws WorkflowLaunchException~
        var launcher = new FailingWorkflowLauncher(webhookId, workflowId);

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(services =>
            {
                services.AddSingleton<IWorkflowLauncher>(launcher);
            }));

        var client = factory.CreateClient();

        // Register the webhook~
        await client.PostAsJsonAsync("/api/webhooks", new
        {
            webhookId,
            workflowDefinitionId = workflowId,
            allowedMethods = new[] { "POST" },
        });

        // Trigger it~
        var response = await client.PostAsJsonAsync(
            $"/webhooks/{webhookId}",
            new { @event = "unknown" });

        // Assert~
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError,
            "WorkflowLaunchException should cause a 500 response~ ");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("error", out var errorEl).Should().BeTrue(
            "error response body should contain an 'error' field~");
        // CopilotNote: The error message must NOT contain internal exception details~
        errorEl.GetString().Should().Be("Failed to launch workflow execution.",
            "sanitised error message should be returned (not the internal exception)~ ");
    }

    // =========================================================================
    // Helpers — minimal workflow definition builder ️
    // =========================================================================

    private static WorkflowDefinition BuildMinimalDefinition(Guid id) =>
        new WorkflowDefinition(
            Id: id,
            Name: "Minimal Test Workflow",
            Description: null,
            Version: new Version(1, 0),
            Nodes: LanguageExt.Arr<NodeDefinition>.Empty,
            Connections: LanguageExt.Arr<ConnectionDefinition>.Empty,
            Variables: HashMap<string, VariableDefinition>.Empty,
            Trigger: null,
            ErrorHandling: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: null,
            Tags: null);
}

// =========================================================================
// Test doubles
// =========================================================================

/// <summary>
///  Stub repo that returns a pre-built workflow definition by any Guid~ .
/// </summary>
internal sealed class StubWorkflowRepository : IWorkflowRepository
{
    private readonly WorkflowDefinition _definition;

    public StubWorkflowRepository(WorkflowDefinition definition)
        => _definition = definition;

    public Task<WorkflowDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult<WorkflowDefinition?>(_definition);

    public Task<WorkflowDefinition?> GetByIdAsync(Guid id, bool includeDeleted, CancellationToken ct = default)
        => GetByIdAsync(id, ct);

    public Task<Guid> CreateAsync(WorkflowDefinition definition, CancellationToken ct = default) => throw new NotImplementedException();
    public Task UpdateAsync(Guid id, WorkflowDefinition definition, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<bool> PurgeAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<bool> RestoreAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Workflow.Persistence.Models.PagedResult<WorkflowDefinition>> GetAllAsync(Workflow.Persistence.Models.WorkflowFilter filter, Workflow.Persistence.Models.Pagination pagination, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<System.Collections.Generic.IReadOnlyList<WorkflowDefinition>> SearchAsync(string query, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
}

/// <summary>
///  Launcher that always returns a fixed execution ID~ ✅.
/// </summary>
internal sealed class FixedSuccessLauncher : IWorkflowLauncher
{
    private readonly Guid _executionId;

    public FixedSuccessLauncher(Guid executionId) => _executionId = executionId;

    public Task<Guid> LaunchAsync(
        WebhookRegistration registration,
        IReadOnlyDictionary<string, object?> inputs,
        CancellationToken ct = default)
        => Task.FromResult(_executionId);
}

/// <summary>
///  Launcher that always throws <see cref="WorkflowLaunchException"/> — simulates
/// a missing workflow definition or supervisor failure~ .
/// </summary>
internal sealed class FailingWorkflowLauncher : IWorkflowLauncher
{
    private readonly string _webhookId;
    private readonly Guid _workflowId;

    public FailingWorkflowLauncher(string webhookId, Guid workflowId)
    {
        _webhookId = webhookId;
        _workflowId = workflowId;
    }

    public Task<Guid> LaunchAsync(
        WebhookRegistration registration,
        IReadOnlyDictionary<string, object?> inputs,
        CancellationToken ct = default)
        => throw new WorkflowLaunchException(
            _webhookId,
            _workflowId,
            $"Workflow definition '{_workflowId}' was not found.");
}
