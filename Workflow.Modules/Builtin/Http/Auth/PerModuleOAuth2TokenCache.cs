// <copyright file="PerModuleOAuth2TokenCache.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Http.Auth;

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 💾 Per-module-instance OAuth2 token cache — simplest, safest default scope~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: Held as a field on a single <see cref="HttpRequestModule"/> instance. Two different
/// module instances → two separate caches → two token fetches even with identical auth properties.
/// <paramref name="executionId"/> is ignored — only <see cref="OAuth2TokenCacheKey"/> matters here~ 🌸
/// </remarks>
public sealed class PerModuleOAuth2TokenCache : IOAuth2TokenCache
{
    private readonly ConcurrentDictionary<OAuth2TokenCacheKey, CachedOAuth2Token> _store = new();

    /// <inheritdoc />
    public Task<CachedOAuth2Token?> GetAsync(Guid executionId, OAuth2TokenCacheKey key, CancellationToken cancellationToken)
    {
        if (_store.TryGetValue(key, out var token) && !token.IsExpired)
        {
            return Task.FromResult<CachedOAuth2Token?>(token);
        }

        // Lazy-cleanup expired entries so the cache doesn't grow unbounded~ 🧹
        if (token is not null && token.IsExpired)
        {
            _store.TryRemove(key, out _);
        }

        return Task.FromResult<CachedOAuth2Token?>(null);
    }

    /// <inheritdoc />
    public Task SetAsync(Guid executionId, OAuth2TokenCacheKey key, CachedOAuth2Token token, CancellationToken cancellationToken)
    {
        _store[key] = token;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task InvalidateAsync(Guid executionId, OAuth2TokenCacheKey key, CancellationToken cancellationToken)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

