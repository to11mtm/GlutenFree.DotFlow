// <copyright file="IWorkflowLauncher.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Webhooks;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Workflow.Core.Models;

/// <summary>
///  Phase 2.3.6 — Abstraction over "start a workflow execution and return its ID"~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// Decoupled from the Akka.NET actor system so the webhook trigger endpoint is unit-testable
/// without spinning up an <c>ActorSystem</c>. A real <c>ActorWorkflowLauncher</c> will be
/// added in Phase 2.3.8 when the API is fully wired to the engine~
/// </para>
/// </remarks>
public interface IWorkflowLauncher
{
    /// <summary>
    /// Start a new workflow execution and return its assigned execution ID~ .
    /// </summary>
    /// <param name="registration">The webhook registration that triggered the launch.</param>
    /// <param name="inputs">
    /// Initial inputs — should always contain the <c>"__webhook__"</c> key with
    /// body/headers/query/method/receivedAt~
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new execution ID.</returns>
    Task<Guid> LaunchAsync(
        WebhookRegistration registration,
        IReadOnlyDictionary<string, object?> inputs,
        CancellationToken ct = default);
}

/// <summary>
///  Phase 2.3.6 — V1 stub launcher: generates a new execution ID without starting a real workflow.
/// </summary>
/// <remarks>
/// <para>
/// <b>[TestingOnly]</b> — Kept in production code for tests and API-only deployments where the
/// engine is a separate service, but <see cref="ActorWorkflowLauncher"/> is the default
/// in production DI since Phase 2.3.9~
/// </para>
/// <para>
/// CopilotNote: Useful in WebApplicationFactory-based API tests to decouple webhook routing
/// tests from the actor system. That's the only reason this stays~
/// </para>
/// </remarks>
public sealed class NullWorkflowLauncher : IWorkflowLauncher
{
    /// <inheritdoc />
    public Task<Guid> LaunchAsync(
        WebhookRegistration registration,
        IReadOnlyDictionary<string, object?> inputs,
        CancellationToken ct = default)
        => Task.FromResult(Guid.NewGuid());
}

