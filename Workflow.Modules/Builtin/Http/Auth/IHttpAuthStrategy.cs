// <copyright file="IHttpAuthStrategy.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Http.Auth;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🔐 Phase 2.3.2 — Pluggable HTTP authentication strategy contract~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: The strategy is invoked *after* the <see cref="HttpRequestMessage"/>
/// is built but *before* it's sent. Implementations may mutate headers AND the URL
/// (api-key-in-query needs the latter)~ 🧠
/// </para>
/// <para>
/// Stateless, allocation-light strategies for V1: Basic / Bearer / ApiKey.
/// OAuth2 (2.3.3) brings the first async/IO strategy — hence the async signature now~ 🌸
/// </para>
/// </remarks>
public interface IHttpAuthStrategy
{
    /// <summary>
    /// Apply auth to the request — may mutate headers and/or the request URI~ 🔧.
    /// </summary>
    /// <param name="request">The request being built.</param>
    /// <param name="context">Module execution context (gives access to logger, services, etc.).</param>
    /// <param name="cancellationToken">Cancellation token (used by async strategies like OAuth2 token-fetch).</param>
    /// <returns>A task that completes when auth has been applied.</returns>
    Task ApplyAsync(HttpRequestMessage request, ModuleExecutionContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Phase 2.3.3 — hook for refresh-on-401 logic. The module calls this after a 401 response;
    /// implementations should invalidate any cached credentials and return <c>true</c> if a
    /// re-apply-and-retry is warranted. Default: <c>false</c> (no retry)~ 🔄.
    /// </summary>
    /// <param name="context">Module execution context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when the caller should re-apply auth + resend once; <c>false</c> otherwise.</returns>
    Task<bool> InvalidateAndPrepareRetryAsync(ModuleExecutionContext context, CancellationToken cancellationToken)
        => Task.FromResult(false);
}

/// <summary>
/// 🔐 Basic Auth — <c>Authorization: Basic base64(user:pass)</c>~ 🎀.
/// </summary>
public sealed class BasicAuthStrategy : IHttpAuthStrategy
{
    private readonly string _username;
    private readonly string _password;

    public BasicAuthStrategy(string username, string password)
    {
        if (string.IsNullOrEmpty(username))
        {
            throw new ArgumentException("Basic auth requires a non-empty username~ 💔", nameof(username));
        }

        _username = username;
        _password = password ?? string.Empty;
    }

    /// <inheritdoc />
    public Task ApplyAsync(HttpRequestMessage request, ModuleExecutionContext context, CancellationToken cancellationToken)
    {
        var raw = $"{_username}:{_password}";
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", b64);
        return Task.CompletedTask;
    }
}

/// <summary>
/// 🔐 Bearer token — <c>Authorization: Bearer {token}</c>~ 🎟️.
/// </summary>
public sealed class BearerAuthStrategy : IHttpAuthStrategy
{
    private readonly string _token;

    public BearerAuthStrategy(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new ArgumentException("Bearer auth requires a non-empty token~ 💔", nameof(token));
        }

        _token = token;
    }

    /// <inheritdoc />
    public Task ApplyAsync(HttpRequestMessage request, ModuleExecutionContext context, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return Task.CompletedTask;
    }
}

/// <summary>
/// 🔐 API Key — placed in either a header (default) or a query-string parameter~ 🗝️.
/// </summary>
public sealed class ApiKeyAuthStrategy : IHttpAuthStrategy
{
    /// <summary>Where to inject the API key (<c>header</c> or <c>query</c>)~ 📍.</summary>
    public enum Location
    {
        Header,
        Query,
    }

    private readonly string _apiKey;
    private readonly string _name;
    private readonly Location _location;

    public ApiKeyAuthStrategy(string apiKey, string? name = null, Location location = Location.Header)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new ArgumentException("API key auth requires a non-empty key~ 💔", nameof(apiKey));
        }

        _apiKey = apiKey;
        _name = string.IsNullOrWhiteSpace(name) ? "X-API-Key" : name!;
        _location = location;
    }

    /// <inheritdoc />
    public Task ApplyAsync(HttpRequestMessage request, ModuleExecutionContext context, CancellationToken cancellationToken)
    {
        if (_location == Location.Header)
        {
            // Remove any pre-existing header of the same name so the auth value wins deterministically~
            request.Headers.Remove(_name);
            request.Headers.TryAddWithoutValidation(_name, _apiKey);
            return Task.CompletedTask;
        }

        // Query-string placement — rebuild the URI with the API key appended~ 🔗
        var original = request.RequestUri
            ?? throw new InvalidOperationException("ApiKeyAuthStrategy: request URI is null~ 💔");

        var ub = new UriBuilder(original);
        var qs = HttpUtility.ParseQueryString(ub.Query);
        qs[_name] = _apiKey;
        ub.Query = qs.ToString();

        request.RequestUri = ub.Uri;
        return Task.CompletedTask;
    }
}

/// <summary>
/// 🚪 No-op auth — used when <c>authType = none</c>~ 🌸.
/// </summary>
public sealed class NoAuthStrategy : IHttpAuthStrategy
{
    /// <summary>Singleton shared instance (stateless)~ ✨.</summary>
    public static readonly NoAuthStrategy Instance = new();

    /// <inheritdoc />
    public Task ApplyAsync(HttpRequestMessage request, ModuleExecutionContext context, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

/// <summary>
/// 🔐 Factory + redaction utilities for auth strategies~ 🛠️💖.
/// </summary>
public static class HttpAuthStrategyFactory
{
    /// <summary>Header names that must be redacted in logs (case-insensitive)~ 🔒.</summary>
    private static readonly System.Collections.Generic.HashSet<string> _redactedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Proxy-Authorization",
        "X-API-Key",
        "X-Api-Key",
        "X-Auth-Token",
        "X-Access-Token",
        "Cookie",
        "Set-Cookie",
    };

    /// <summary>
    /// Build the strategy from properties on a <see cref="HttpRequestModule"/>~ 🔧.
    /// </summary>
    /// <param name="properties">The module properties.</param>
    /// <param name="error">When the return is null, this carries a user-readable error message.</param>
    /// <returns>The selected strategy, or null when auth could not be configured.</returns>
    public static IHttpAuthStrategy? FromProperties(
        System.Collections.Generic.IReadOnlyDictionary<string, object?> properties,
        out string? error)
        => FromProperties(properties, context: null, perModuleCache: null, out error);

    /// <summary>
    /// Phase 2.3.3 overload — accepts execution context + per-module cache for OAuth2 strategies~ 🔑.
    /// </summary>
    /// <param name="properties">The module properties.</param>
    /// <param name="context">Module execution context (required for OAuth2 — gives DI + ExecutionId).</param>
    /// <param name="perModuleCache">Per-module OAuth2 token cache (used when <c>oauth2TokenCacheScope = module</c>).</param>
    /// <param name="error">When the return is null, this carries a user-readable error message.</param>
    /// <returns>The selected strategy, or null when auth could not be configured.</returns>
    public static IHttpAuthStrategy? FromProperties(
        System.Collections.Generic.IReadOnlyDictionary<string, object?> properties,
        ModuleExecutionContext? context,
        IOAuth2TokenCache? perModuleCache,
        out string? error)
    {
        error = null;
        var authType = GetString(properties, "authType")?.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(authType) || authType == "none")
        {
            return NoAuthStrategy.Instance;
        }

        switch (authType)
        {
            case "basic":
                var user = GetString(properties, "username");
                var pass = GetString(properties, "password") ?? string.Empty;
                if (string.IsNullOrEmpty(user))
                {
                    error = "Basic auth requires 'username' property~ 💔";
                    return null;
                }

                return new BasicAuthStrategy(user!, pass);

            case "bearer":
                var token = GetString(properties, "bearerToken");
                if (string.IsNullOrEmpty(token))
                {
                    error = "Bearer auth requires 'bearerToken' property~ 💔";
                    return null;
                }

                return new BearerAuthStrategy(token!);

            case "apikey":
            case "api-key":
            case "api_key":
                var key = GetString(properties, "apiKey");
                if (string.IsNullOrEmpty(key))
                {
                    error = "API key auth requires 'apiKey' property~ 💔";
                    return null;
                }

                var headerName = GetString(properties, "apiKeyHeader");
                var loc = (GetString(properties, "apiKeyLocation") ?? "header").Trim().ToLowerInvariant();
                var location = loc == "query"
                    ? ApiKeyAuthStrategy.Location.Query
                    : ApiKeyAuthStrategy.Location.Header;

                return new ApiKeyAuthStrategy(key!, headerName, location);

            case "oauth2":
                return BuildOAuth2Strategy(properties, context, perModuleCache, out error);

            default:
                error = $"Unknown authType '{authType}' (valid: none/basic/bearer/apikey/oauth2)~ 💔";
                return null;
        }
    }

    /// <summary>Build the OAuth2 client-credentials strategy from properties + chosen cache scope~ 🔑.</summary>
    private static IHttpAuthStrategy? BuildOAuth2Strategy(
        System.Collections.Generic.IReadOnlyDictionary<string, object?> properties,
        ModuleExecutionContext? context,
        IOAuth2TokenCache? perModuleCache,
        out string? error)
    {
        error = null;

        if (context is null)
        {
            error = "OAuth2 requires a ModuleExecutionContext (use the 4-arg FromProperties overload)~ 💔";
            return null;
        }

        var tokenUrl = GetString(properties, "oauth2TokenUrl");
        var clientId = GetString(properties, "oauth2ClientId");
        var clientSecret = GetString(properties, "oauth2ClientSecret");

        if (string.IsNullOrWhiteSpace(tokenUrl) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            error = "OAuth2 requires 'oauth2TokenUrl', 'oauth2ClientId' and 'oauth2ClientSecret' properties~ 💔";
            return null;
        }

        var settings = new OAuth2Settings(
            tokenUrl!,
            clientId!,
            clientSecret!,
            Scope: GetString(properties, "oauth2Scope"),
            Audience: GetString(properties, "oauth2Audience"));

        // Pick cache by scope (default: module)~
        var scope = (GetString(properties, "oauth2TokenCacheScope") ?? "module").Trim().ToLowerInvariant();
        IOAuth2TokenCache? cache;
        switch (scope)
        {
            case "module":
                cache = perModuleCache;
                if (cache is null)
                {
                    error = "OAuth2 module-scope cache is null — caller must supply a PerModuleOAuth2TokenCache~ 💔";
                    return null;
                }

                break;
            case "pipeline":
                cache = context.Services.GetService(typeof(PerPipelineOAuth2TokenCache)) as IOAuth2TokenCache
                    ?? context.Services.GetService(typeof(IOAuth2TokenCache)) as IOAuth2TokenCache;
                if (cache is null)
                {
                    error = "OAuth2 pipeline-scope requires PerPipelineOAuth2TokenCache in DI (registered by AddHttpModules)~ 💔";
                    return null;
                }

                break;
            default:
                error = $"Unknown oauth2TokenCacheScope '{scope}' (valid: module/pipeline; singleton/persisted ship in 2.3.P3)~ 💔";
                return null;
        }

        return new OAuth2ClientCredentialsStrategy(settings, cache);
    }

    /// <summary>Returns true if a header name carries credentials that must be redacted from logs~ 🔒.</summary>
    public static bool IsRedactedHeader(string name) => _redactedHeaders.Contains(name);

    /// <summary>Return a header-value-safe representation for logging — redacts known credential headers~ 🔒.</summary>
    public static string RedactForLog(string name, string value)
        => IsRedactedHeader(name)
            ? "***REDACTED***"
            : value;

    /// <summary>Redact an entire request-headers snapshot for diagnostic logging~ 🔒.</summary>
    public static IReadOnlyDictionary<string, string> RedactHeaders(IEnumerable<KeyValuePair<string, string>> headers)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in headers)
        {
            dict[kv.Key] = RedactForLog(kv.Key, kv.Value);
        }

        return dict;
    }

    private static string? GetString(System.Collections.Generic.IReadOnlyDictionary<string, object?> props, string key)
        => props.TryGetValue(key, out var v) && v is not null ? v.ToString() : null;
}

