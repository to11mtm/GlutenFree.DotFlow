// <copyright file="AuthMessageHandler.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Api;

using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 🔐 Phase 3.3.a.0 — A <see cref="DelegatingHandler"/> that stamps the current credential from
/// <see cref="AuthState"/> onto every REST request: <c>Authorization: Bearer</c> for a JWT, or
/// the <c>X-API-Key</c> header for an API key (D9)~ ✨.
/// </summary>
public sealed class AuthMessageHandler : DelegatingHandler
{
    /// <summary>The API-key header name (matches <c>AuthConstants.ApiKeyHeader</c>)~ 🔑.</summary>
    public const string ApiKeyHeader = "X-API-Key";

    private readonly AuthState auth;

    /// <summary>Initializes a new instance of the <see cref="AuthMessageHandler"/> class~ 🔐.</summary>
    /// <param name="auth">The auth state.</param>
    public AuthMessageHandler(AuthState auth) => this.auth = auth;

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(this.auth.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this.auth.Token);
        }
        else if (!string.IsNullOrWhiteSpace(this.auth.ApiKey))
        {
            request.Headers.Remove(ApiKeyHeader);
            request.Headers.Add(ApiKeyHeader, this.auth.ApiKey);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
