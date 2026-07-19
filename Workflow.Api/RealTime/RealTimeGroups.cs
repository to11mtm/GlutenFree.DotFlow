// <copyright file="RealTimeGroups.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.RealTime;

using System;

/// <summary>
/// 📡 Phase 3.2 — Canonical SignalR group names + subscription keys for the real-time hub. A
/// subscription key is the group name; the two are kept identical so the connection tracker and
/// the broadcast fan-out never diverge~ ✨.
/// </summary>
public static class RealTimeGroups
{
    /// <summary>The firehose group — every event (admin-gated)~ 🌐.</summary>
    public const string All = "all";

    /// <summary>The group for a specific execution's events~ ⚡.</summary>
    /// <param name="executionId">The execution id.</param>
    /// <returns>The group name.</returns>
    public static string Execution(Guid executionId) => $"execution:{executionId}";

    /// <summary>The group for all executions of a specific workflow~ 📋.</summary>
    /// <param name="workflowId">The workflow id.</param>
    /// <returns>The group name.</returns>
    public static string Workflow(Guid workflowId) => $"workflow:{workflowId}";
}
