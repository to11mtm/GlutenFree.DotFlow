// <copyright file="PerPipelineOAuth2TokenCache.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Http.Auth;

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 💾 Pipeline-scoped OAuth2 token cache — DI singleton; tokens are shared across all
/// HTTP nodes inside the same <c>WorkflowExecution</c>~ ✨🪄.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Effective key is <c>(executionId, OAuth2TokenCacheKey)</c>. A workflow that calls
/// the same OAuth2-protected API ten times will perform exactly one token fetch — but a different
/// workflow execution with identical auth properties will fetch its own token~ 🌸
/// </para>
/// <para>
/// Cross-workflow / persisted scopes are tracked in <c>2.3.P3</c>; this implementation deliberately
/// keys on <c>executionId</c> so V1 cannot leak tokens across workflows by accident~ 🛡️
/// </para>
/// </remarks>
public sealed class PerPipelineOAuth2TokenCache : IOAuth2TokenCache
{
    private readonly ConcurrentDictionary<(Guid ExecutionId, OAuth2TokenCacheKey Key), CachedOAuth2Token> _store = new();

    /// <inheritdoc />
    public Task<CachedOAuth2Token?> GetAsync(Guid executionId, OAuth2TokenCacheKey key, CancellationToken cancellationToken)
    {
        var compositeKey = (executionId, key);
        if (_store.TryGetValue(compositeKey, out var token) && !token.IsExpired)
        {
            return Task.FromResult<CachedOAuth2Token?>(token);
        }

        if (token is not null && token.IsExpired)
        {
            _store.TryRemove(compositeKey, out _);
        }

        return Task.FromResult<CachedOAuth2Token?>(null);
    }

    /// <inheritdoc />
    public Task SetAsync(Guid executionId, OAuth2TokenCacheKey key, CachedOAuth2Token token, CancellationToken cancellationToken)
    {
        _store[(executionId, key)] = token;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task InvalidateAsync(Guid executionId, OAuth2TokenCacheKey key, CancellationToken cancellationToken)
    {
        _store.TryRemove((executionId, key), out _);
        return Task.CompletedTask;
    }
}

