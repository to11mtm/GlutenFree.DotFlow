// <copyright file="IOAuth2TokenCache.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Http.Auth;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 💾 Phase 2.3.3 — OAuth2 token cache abstraction~ ✨🔑.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Two built-in scopes for V1 (per <see href="#Q1"/>):
/// <list type="bullet">
///   <item><description><see cref="PerModuleOAuth2TokenCache"/> — fresh dictionary held on a single
///     <see cref="HttpRequestModule"/> instance. <paramref name="executionId"/> is ignored — keys
///     are global to the cache instance.</description></item>
///   <item><description><see cref="PerPipelineOAuth2TokenCache"/> — DI singleton; effective key is
///     <c>(executionId, authority, clientId, scope)</c> so tokens are reused within a single
///     <c>WorkflowExecution</c> but never leak across workflows.</description></item>
/// </list>
/// Cross-workflow singleton + persisted scopes ship in <c>2.3.P3</c>~ 🌸
/// </para>
/// </remarks>
public interface IOAuth2TokenCache
{
    /// <summary>Try to read a cached token. Returns null when missing/expired~ 🔍.</summary>
    Task<CachedOAuth2Token?> GetAsync(Guid executionId, OAuth2TokenCacheKey key, CancellationToken cancellationToken);

    /// <summary>Cache a freshly-fetched token~ 💾.</summary>
    Task SetAsync(Guid executionId, OAuth2TokenCacheKey key, CachedOAuth2Token token, CancellationToken cancellationToken);

    /// <summary>Force-invalidate a cached token (used on 401 refresh-and-retry)~ 🗑️.</summary>
    Task InvalidateAsync(Guid executionId, OAuth2TokenCacheKey key, CancellationToken cancellationToken);
}

/// <summary>
/// Cache key — the three fields the OAuth2 spec says identify a unique token tuple~ 🔑.
/// </summary>
/// <param name="Authority">Token endpoint URL (full URL — different paths on the same host are distinct authorities).</param>
/// <param name="ClientId">OAuth2 client identifier.</param>
/// <param name="Scope">Requested scope (may be empty when the server allows it).</param>
public sealed record OAuth2TokenCacheKey(string Authority, string ClientId, string Scope);

/// <summary>
/// A cached OAuth2 access token with computed expiry~ 🎟️.
/// </summary>
/// <param name="AccessToken">The bearer token value to send in the Authorization header.</param>
/// <param name="TokenType">Token type (must be <c>Bearer</c> for V1).</param>
/// <param name="ExpiresAt">Absolute UTC time at which the token is considered expired
/// (<c>fetchTime + expires_in - 30s safety margin</c> per plan).</param>
public sealed record CachedOAuth2Token(string AccessToken, string TokenType, DateTimeOffset ExpiresAt)
{
    /// <summary>Is the token expired right now?~ ⏰.</summary>
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
}

