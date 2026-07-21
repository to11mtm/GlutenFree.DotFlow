// <copyright file="IWebhookResponseStrategy.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Webhooks;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

/// <summary>
/// 🎯 Phase 2.3.6 — Strategy that decides how to respond to the HTTP caller after a webhook fires~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// V1 ships one implementation: <see cref="Async202ResponseStrategy"/> — immediately returns
/// <c>202 Accepted</c> with the <c>executionId</c> and lets the workflow run asynchronously.
/// </para>
/// <para>
/// CopilotNote: Future strategies (2.3.P2) — <c>WaitForFirstOutputStrategy</c> and
/// <c>WaitForCompletionStrategy</c> — plug in here without touching the V1 controller or
/// dispatcher. The calling endpoint resolves the strategy from DI (default = Async202)~ 🧠
/// </para>
/// </remarks>
public interface IWebhookResponseStrategy
{
    /// <summary>
    /// Write the HTTP response for a successfully triggered webhook execution~ ✅.
    /// </summary>
    /// <param name="context">The current <see cref="HttpContext"/>.</param>
    /// <param name="executionId">The execution ID returned by the workflow launcher.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RespondAsync(HttpContext context, Guid executionId, CancellationToken ct = default);
}

/// <summary>
/// 📬 Phase 2.3.6 — Default response strategy: <c>202 Accepted</c> with <c>{ executionId }</c>.
/// The workflow runs asynchronously and the caller does not wait for results~ 🌸.
/// </summary>
/// <remarks>
/// CopilotNote: This is the V1 default registered in DI. Swap it out per-request in future
/// advanced scenarios (2.3.P2) by overriding the DI registration in test or per-host config~ 💡
/// </remarks>
public sealed class Async202ResponseStrategy : IWebhookResponseStrategy
{
    /// <inheritdoc />
    public Task RespondAsync(HttpContext context, Guid executionId, CancellationToken ct = default)
    {
        context.Response.StatusCode = StatusCodes.Status202Accepted;
        return context.Response.WriteAsJsonAsync(new { executionId }, ct);
    }
}

