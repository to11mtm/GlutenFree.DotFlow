// <copyright file="HttpResiliencePipelineFactory.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Http.Resilience;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

/// <summary>
/// 🔄 Phase 2.3.4 — Builds + caches Polly v8 resilience pipelines for HTTP requests~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Pipelines are cached per-config-hash on the factory instance so the
/// <see cref="Polly.CircuitBreaker.CircuitBreakerStrategyOptions{TResult}"/> state actually
/// persists across calls — a circuit breaker that resets every call is useless~ 🌸
/// </para>
/// <para>
/// Per-call mutable state (attempt count, current circuit state) is passed via
/// <see cref="ResilienceContext.Properties"/> using <see cref="StateKey"/>. This keeps the
/// pipeline itself stateless w.r.t. individual calls~ 🧠
/// </para>
/// </remarks>
public sealed class HttpResiliencePipelineFactory
{
    /// <summary>Property key used to thread per-call state through the Polly pipeline~ 🔑.</summary>
    public static readonly ResiliencePropertyKey<ResilienceCallState> StateKey = new("dotflow.http.resilience.state");

    private readonly ConcurrentDictionary<string, ResiliencePipeline<HttpResponseMessage>> _cache = new();

    /// <summary>Get-or-build a pipeline for the given config~ 🛠️.</summary>
    public ResiliencePipeline<HttpResponseMessage> GetOrBuild(HttpResilienceConfig config, ILogger? logger = null)
        => _cache.GetOrAdd(config.CacheKey, _ => Build(config, logger));

    /// <summary>
    /// Parse <see cref="HttpRequestModule"/> properties into a config record~ 📋.
    /// Returns <c>null</c> only when there's a parse error (carried in <paramref name="error"/>).
    /// </summary>
    public static HttpResilienceConfig? ParseFromProperties(
        IReadOnlyDictionary<string, object?> properties,
        out string? error)
    {
        error = null;

        var retryCount = TryInt(properties, "retryCount") ?? 0;
        if (retryCount < 0)
        {
            error = "retryCount must be >= 0~ 💔";
            return null;
        }

        var retryDelaySeconds = TryDouble(properties, "retryDelaySeconds") ?? 1.0;
        if (retryDelaySeconds < 0)
        {
            error = "retryDelaySeconds must be >= 0~ 💔";
            return null;
        }

        var maxBackoffSeconds = TryDouble(properties, "maxRetryBackoffSeconds") ?? 60.0;
        if (maxBackoffSeconds <= 0)
        {
            error = "maxRetryBackoffSeconds must be > 0~ 💔";
            return null;
        }

        var backoffName = (TryString(properties, "retryBackoff") ?? "exponential").Trim().ToLowerInvariant();
        DelayBackoffType backoffType = backoffName switch
        {
            "linear" => DelayBackoffType.Linear,
            "exponential" => DelayBackoffType.Exponential,
            "constant" => DelayBackoffType.Constant,
            _ => DelayBackoffType.Exponential, // "fibonacci" not in Polly v8 — fall back to exponential
        };

        // Default retry status set per plan~
        var defaultRetryStatuses = new System.Collections.Generic.HashSet<int> { 408, 429, 500, 502, 503, 504 };
        var retryStatuses = TryIntSet(properties, "retryOnStatusCodes") ?? defaultRetryStatuses;

        var cbThreshold = TryInt(properties, "circuitBreakerFailureThreshold") ?? 0;
        var cbSamplingSeconds = TryDouble(properties, "circuitBreakerSamplingDurationSeconds") ?? 30.0;
        if (cbThreshold < 0)
        {
            error = "circuitBreakerFailureThreshold must be >= 0~ 💔";
            return null;
        }

        return new HttpResilienceConfig(
            retryCount,
            backoffType,
            TimeSpan.FromSeconds(retryDelaySeconds),
            TimeSpan.FromSeconds(maxBackoffSeconds),
            retryStatuses,
            cbThreshold,
            TimeSpan.FromSeconds(cbSamplingSeconds));
    }

    private static ResiliencePipeline<HttpResponseMessage> Build(HttpResilienceConfig config, ILogger? logger)
    {
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();

        // 🔁 Retry strategy~
        if (config.RetryCount > 0)
        {
            builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = config.RetryCount,
                ShouldHandle = args =>
                {
                    if (args.Outcome.Exception is HttpRequestException)
                    {
                        return ValueTask.FromResult(true);
                    }

                    var response = args.Outcome.Result;
                    return ValueTask.FromResult(
                        response is not null && config.RetryOnStatusCodes.Contains((int)response.StatusCode));
                },
                BackoffType = config.BackoffType,
                Delay = config.RetryDelay,
                MaxDelay = config.MaxRetryBackoff,
                UseJitter = true,
                DelayGenerator = args =>
                {
                    // 🎀 Honour Retry-After header (cap at MaxRetryBackoff)~
                    var headerDelay = TryReadRetryAfter(args.Outcome.Result);
                    if (headerDelay.HasValue)
                    {
                        if (headerDelay.Value > config.MaxRetryBackoff)
                        {
                            logger?.LogWarning(
                                "⚠️ Retry-After header value {HeaderDelay} exceeds maxRetryBackoffSeconds {MaxBackoff}; falling back to configured backoff~",
                                headerDelay.Value, config.MaxRetryBackoff);
                            return ValueTask.FromResult<TimeSpan?>(null);
                        }

                        return ValueTask.FromResult<TimeSpan?>(headerDelay.Value);
                    }

                    return ValueTask.FromResult<TimeSpan?>(null); // Use Polly's default backoff
                },
                OnRetry = args =>
                {
                    if (args.Context.Properties.TryGetValue(StateKey, out var state) && state is not null)
                    {
                        // AttemptNumber is 0-based for the FIRST retry — total attempts = AttemptNumber + 2
                        // (1 original + N retries — but Polly's AttemptNumber starts at 0 for the first retry)
                        state.AttemptCount = args.AttemptNumber + 2;
                        state.WasRetried = true;
                    }

                    return default;
                },
            });
        }

        // 🛑 Circuit breaker~
        if (config.CircuitBreakerFailureThreshold > 0)
        {
            builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = 1.0, // Any failure within the window with min throughput trips~
                MinimumThroughput = Math.Max(2, config.CircuitBreakerFailureThreshold), // Polly requires >= 2
                SamplingDuration = config.CircuitBreakerSamplingDuration,
                BreakDuration = TimeSpan.FromMilliseconds(500), // Short for V1; configurable later~
                ShouldHandle = args =>
                {
                    if (args.Outcome.Exception is HttpRequestException)
                    {
                        return ValueTask.FromResult(true);
                    }

                    var response = args.Outcome.Result;
                    return ValueTask.FromResult(
                        response is not null && config.RetryOnStatusCodes.Contains((int)response.StatusCode));
                },
                OnOpened = args =>
                {
                    if (args.Context.Properties.TryGetValue(StateKey, out var state) && state is not null)
                    {
                        state.CircuitState = "open";
                    }

                    return default;
                },
                OnClosed = args =>
                {
                    if (args.Context.Properties.TryGetValue(StateKey, out var state) && state is not null)
                    {
                        state.CircuitState = "closed";
                    }

                    return default;
                },
                OnHalfOpened = args =>
                {
                    if (args.Context.Properties.TryGetValue(StateKey, out var state) && state is not null)
                    {
                        state.CircuitState = "halfopen";
                    }

                    return default;
                },
            });
        }

        return builder.Build();
    }

    /// <summary>Parse <c>Retry-After</c> header — supports seconds and HTTP-date forms~ ⏱️.</summary>
    internal static TimeSpan? TryReadRetryAfter(HttpResponseMessage? response)
    {
        var ra = response?.Headers?.RetryAfter;
        if (ra is null)
        {
            return null;
        }

        if (ra.Delta.HasValue)
        {
            return ra.Delta.Value;
        }

        if (ra.Date.HasValue)
        {
            var delta = ra.Date.Value - DateTimeOffset.UtcNow;
            return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
        }

        return null;
    }

    private static int? TryInt(IReadOnlyDictionary<string, object?> p, string k)
        => p.TryGetValue(k, out var v) && v is not null
            ? v switch
            {
                int i => i,
                long l => (int)l,
                double d => (int)d,
                string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => (int?)null,
            }
            : null;

    private static double? TryDouble(IReadOnlyDictionary<string, object?> p, string k)
        => p.TryGetValue(k, out var v) && v is not null
            ? v switch
            {
                double d => d,
                int i => i,
                long l => l,
                string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => (double?)null,
            }
            : null;

    private static string? TryString(IReadOnlyDictionary<string, object?> p, string k)
        => p.TryGetValue(k, out var v) && v is not null ? v.ToString() : null;

    private static System.Collections.Generic.HashSet<int>? TryIntSet(IReadOnlyDictionary<string, object?> p, string k)
    {
        if (!p.TryGetValue(k, out var raw) || raw is null)
        {
            return null;
        }

        var set = new System.Collections.Generic.HashSet<int>();
        switch (raw)
        {
            case IEnumerable<int> ints:
                foreach (var i in ints)
                {
                    set.Add(i);
                }

                break;
            case IEnumerable<object?> objs:
                foreach (var o in objs)
                {
                    if (o is int i)
                    {
                        set.Add(i);
                    }
                    else if (int.TryParse(o?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    {
                        set.Add(parsed);
                    }
                }

                break;
            default:
                return null;
        }

        return set;
    }
}

/// <summary>
/// 📊 Mutable per-call state threaded through the Polly pipeline via <see cref="ResilienceContext"/>~ 🔧.
/// </summary>
public sealed class ResilienceCallState
{
    /// <summary>Total attempts made (1 = no retry; 2 = retried once, etc.)~ 🔢.</summary>
    public int AttemptCount { get; set; } = 1;

    /// <summary>Was at least one retry attempted?~ 🔁.</summary>
    public bool WasRetried { get; set; }

    /// <summary>Current circuit state: <c>closed</c> / <c>open</c> / <c>halfopen</c>~ 🚦.</summary>
    public string CircuitState { get; set; } = "closed";
}

/// <summary>
/// 📋 Parsed resilience configuration — cacheable key included for pipeline reuse~ 🔑.
/// </summary>
public sealed record HttpResilienceConfig(
    int RetryCount,
    DelayBackoffType BackoffType,
    TimeSpan RetryDelay,
    TimeSpan MaxRetryBackoff,
    System.Collections.Generic.HashSet<int> RetryOnStatusCodes,
    int CircuitBreakerFailureThreshold,
    TimeSpan CircuitBreakerSamplingDuration)
{
    /// <summary>Stable string key for caching pipelines~ 🗝️.</summary>
    public string CacheKey { get; } = string.Create(CultureInfo.InvariantCulture, $"r{RetryCount}|b{BackoffType}|d{RetryDelay.TotalMilliseconds}|m{MaxRetryBackoff.TotalMilliseconds}|s{string.Join(',', RetryOnStatusCodes.OrderBy(x => x))}|c{CircuitBreakerFailureThreshold}|w{CircuitBreakerSamplingDuration.TotalMilliseconds}");
}

