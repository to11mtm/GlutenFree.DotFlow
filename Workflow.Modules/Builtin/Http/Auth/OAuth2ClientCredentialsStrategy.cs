// <copyright file="OAuth2ClientCredentialsStrategy.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Http.Auth;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🔑 Phase 2.3.3 — OAuth2 Client Credentials grant strategy~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Token-fetch hits the configured <c>tokenUrl</c> with
/// <c>application/x-www-form-urlencoded</c> body <c>grant_type=client_credentials</c>
/// + <c>client_id</c> + <c>client_secret</c> + optional <c>scope</c>/<c>audience</c>.
/// Result is cached via <see cref="IOAuth2TokenCache"/> with TTL = <c>expires_in - 30s</c>
/// safety margin (per plan)~ 🧠
/// </para>
/// <para>
/// Failures map to <see cref="OAuth2AuthorizationException"/> with the spec's <c>error</c> code
/// so the calling module can surface a structured failure~ 🛡️
/// </para>
/// </remarks>
public sealed class OAuth2ClientCredentialsStrategy : IHttpAuthStrategy
{
    /// <summary>Safety margin subtracted from <c>expires_in</c> so we refresh slightly early~ 🛡️.</summary>
    private static readonly TimeSpan _refreshSafetyMargin = TimeSpan.FromSeconds(30);

    private readonly OAuth2Settings _settings;
    private readonly IOAuth2TokenCache _cache;
    private readonly OAuth2TokenCacheKey _cacheKey;

    public OAuth2ClientCredentialsStrategy(OAuth2Settings settings, IOAuth2TokenCache cache)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _cacheKey = new OAuth2TokenCacheKey(settings.TokenUrl, settings.ClientId, settings.Scope ?? string.Empty);
    }

    /// <inheritdoc />
    public async Task ApplyAsync(HttpRequestMessage request, ModuleExecutionContext context, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(context, cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
    }

    /// <inheritdoc />
    public Task<bool> InvalidateAndPrepareRetryAsync(ModuleExecutionContext context, CancellationToken cancellationToken)
    {
        // 401 retry path: drop cache so the next ApplyAsync re-fetches~ 🗑️
        _cache.InvalidateAsync(context.ExecutionId, _cacheKey, cancellationToken).GetAwaiter().GetResult();
        return Task.FromResult(true);
    }

    private async Task<CachedOAuth2Token> GetTokenAsync(ModuleExecutionContext context, CancellationToken cancellationToken)
    {
        // 1️⃣ Check cache first~ 🔍
        var cached = await _cache.GetAsync(context.ExecutionId, _cacheKey, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            context.Logger.LogDebug("🔑 OAuth2 cache hit for {Authority}~", _settings.TokenUrl);
            return cached;
        }

        // 2️⃣ Cache miss → fetch fresh~ 🌐
        var fresh = await FetchTokenAsync(context, cancellationToken).ConfigureAwait(false);

        // 3️⃣ Cache + return~ 💾
        await _cache.SetAsync(context.ExecutionId, _cacheKey, fresh, cancellationToken).ConfigureAwait(false);
        return fresh;
    }

    private async Task<CachedOAuth2Token> FetchTokenAsync(ModuleExecutionContext context, CancellationToken cancellationToken)
    {
        var factory = context.Services.GetService<IHttpClientFactory>()
            ?? throw new InvalidOperationException(
                "IHttpClientFactory not available — call services.AddWorkflowModules() at host startup~ 💔");

        using var client = factory.CreateClient(HttpModuleServiceCollectionExtensions.HttpClientName);

        // Build the token-fetch form body~ 📤
        var formFields = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "client_credentials"),
            new("client_id", _settings.ClientId),
            new("client_secret", _settings.ClientSecret),
        };
        if (!string.IsNullOrEmpty(_settings.Scope))
        {
            formFields.Add(new KeyValuePair<string, string>("scope", _settings.Scope!));
        }

        if (!string.IsNullOrEmpty(_settings.Audience))
        {
            formFields.Add(new KeyValuePair<string, string>("audience", _settings.Audience!));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _settings.TokenUrl)
        {
            Content = new FormUrlEncodedContent(formFields),
        };

        context.Logger.LogDebug("🔑 OAuth2 token fetch → {Authority} (clientId={ClientId})", _settings.TokenUrl, _settings.ClientId);

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var bodyBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        // Even error responses are JSON in the OAuth2 spec, so always try to parse~ 📥
        Dictionary<string, JsonElement>? json = null;
        try
        {
            json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(bodyBytes);
        }
        catch (JsonException)
        {
            // Non-JSON response — fall through to status-based error~
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorCode = json is not null && json.TryGetValue("error", out var ec) && ec.ValueKind == JsonValueKind.String
                ? ec.GetString() ?? "unknown_error"
                : $"http_{(int)response.StatusCode}";
            var errorDesc = json is not null && json.TryGetValue("error_description", out var ed) && ed.ValueKind == JsonValueKind.String
                ? ed.GetString()
                : null;

            throw new OAuth2AuthorizationException(errorCode, errorDesc, (int)response.StatusCode);
        }

        if (json is null
            || !json.TryGetValue("access_token", out var tokenEl)
            || tokenEl.ValueKind != JsonValueKind.String)
        {
            throw new OAuth2AuthorizationException("invalid_response", "Token endpoint did not return an access_token", (int)response.StatusCode);
        }

        var accessToken = tokenEl.GetString()!;
        var tokenType = json.TryGetValue("token_type", out var ttEl) && ttEl.ValueKind == JsonValueKind.String
            ? ttEl.GetString() ?? "Bearer"
            : "Bearer";

        if (!string.Equals(tokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            throw new OAuth2AuthorizationException(
                "unsupported_token_type",
                $"token_type '{tokenType}' is not supported (V1 requires Bearer)",
                (int)response.StatusCode);
        }

        var expiresInSeconds = json.TryGetValue("expires_in", out var expEl) && expEl.ValueKind == JsonValueKind.Number
            ? expEl.GetInt32()
            : 3600; // Sensible default if the server omits expires_in~

        var expiresAt = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(expiresInSeconds) - _refreshSafetyMargin;

        return new CachedOAuth2Token(accessToken, tokenType, expiresAt);
    }
}

/// <summary>
/// 🔑 Per-request OAuth2 settings extracted from module properties~ 📋.
/// </summary>
/// <param name="TokenUrl">Token endpoint URL (e.g. <c>https://auth.example.com/oauth/token</c>).</param>
/// <param name="ClientId">OAuth2 client identifier.</param>
/// <param name="ClientSecret">OAuth2 client secret.</param>
/// <param name="Scope">Optional space-separated scopes.</param>
/// <param name="Audience">Optional audience (Auth0-style; not part of the core RFC but common in the wild).</param>
public sealed record OAuth2Settings(
    string TokenUrl,
    string ClientId,
    string ClientSecret,
    string? Scope = null,
    string? Audience = null);

/// <summary>
/// 💔 Structured error thrown when the OAuth2 token endpoint rejects a token-fetch~ 🛡️.
/// </summary>
public sealed class OAuth2AuthorizationException : Exception
{
    public OAuth2AuthorizationException(string errorCode, string? description, int httpStatus)
        : base($"OAuth2 token fetch failed [{errorCode}] (HTTP {httpStatus}): {description ?? "(no description)"}")
    {
        ErrorCode = errorCode;
        Description = description;
        HttpStatus = httpStatus;
    }

    /// <summary>The OAuth2 spec <c>error</c> code (e.g. <c>invalid_client</c>, <c>invalid_scope</c>)~ 🏷️.</summary>
    public string ErrorCode { get; }

    /// <summary>The optional <c>error_description</c> from the server~ 📝.</summary>
    public string? Description { get; }

    /// <summary>HTTP status from the token endpoint response~ 🔢.</summary>
    public int HttpStatus { get; }
}

