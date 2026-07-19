// <copyright file="IConnectionTracker.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.RealTime;

using System.Collections.Generic;

/// <summary>
/// 📡 Phase 3.2 — Thread-safe bookkeeping of hub connections and their subscription keys. Used
/// for <b>metrics</b> and <b>reconnection restore</b> only — SignalR groups remain the source of
/// truth for the actual broadcast fan-out (D7)~ ✨.
/// </summary>
public interface IConnectionTracker
{
    /// <summary>Number of currently-connected clients~ 🔌.</summary>
    int ConnectionCount { get; }

    /// <summary>Total number of active subscriptions across all connections~ 📋.</summary>
    int SubscriptionCount { get; }

    /// <summary>Registers a newly-connected client~ 🔌.</summary>
    /// <param name="connectionId">The SignalR connection id.</param>
    void AddConnection(string connectionId);

    /// <summary>Removes a connection and all its subscriptions~ 👋.</summary>
    /// <param name="connectionId">The SignalR connection id.</param>
    void RemoveConnection(string connectionId);

    /// <summary>Records a subscription for a connection~ ➕.</summary>
    /// <param name="connectionId">The SignalR connection id.</param>
    /// <param name="subscriptionKey">The subscription/group key.</param>
    void AddSubscription(string connectionId, string subscriptionKey);

    /// <summary>Removes a subscription for a connection~ ➖.</summary>
    /// <param name="connectionId">The SignalR connection id.</param>
    /// <param name="subscriptionKey">The subscription/group key.</param>
    void RemoveSubscription(string connectionId, string subscriptionKey);

    /// <summary>Gets the current subscription keys for a connection~ 🔍.</summary>
    /// <param name="connectionId">The SignalR connection id.</param>
    /// <returns>The subscription keys (empty if none/unknown).</returns>
    IReadOnlyCollection<string> GetSubscriptions(string connectionId);
}
