// <copyright file="ConnectionTracker.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.RealTime;

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;

/// <summary>
/// 📡 Phase 3.2 — Thread-safe <see cref="IConnectionTracker"/> backed by a
/// <see cref="ConcurrentDictionary{TKey, TValue}"/> of per-connection subscription sets~ ✨.
/// </summary>
public sealed class ConnectionTracker : IConnectionTracker
{
    private readonly ConcurrentDictionary<string, HashSet<string>> connections = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public int ConnectionCount => this.connections.Count;

    /// <inheritdoc/>
    public int SubscriptionCount
    {
        get
        {
            var total = 0;
            foreach (var set in this.connections.Values)
            {
                lock (set)
                {
                    total += set.Count;
                }
            }

            return total;
        }
    }

    /// <inheritdoc/>
    public void AddConnection(string connectionId)
        => this.connections.TryAdd(connectionId, new HashSet<string>(StringComparer.Ordinal));

    /// <inheritdoc/>
    public void RemoveConnection(string connectionId)
        => this.connections.TryRemove(connectionId, out _);

    /// <inheritdoc/>
    public void AddSubscription(string connectionId, string subscriptionKey)
    {
        var set = this.connections.GetOrAdd(connectionId, _ => new HashSet<string>(StringComparer.Ordinal));
        lock (set)
        {
            set.Add(subscriptionKey);
        }
    }

    /// <inheritdoc/>
    public void RemoveSubscription(string connectionId, string subscriptionKey)
    {
        if (this.connections.TryGetValue(connectionId, out var set))
        {
            lock (set)
            {
                set.Remove(subscriptionKey);
            }
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetSubscriptions(string connectionId)
    {
        if (this.connections.TryGetValue(connectionId, out var set))
        {
            lock (set)
            {
                return set.ToArray();
            }
        }

        return Array.Empty<string>();
    }
}
