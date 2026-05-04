// <copyright file="SubGraphMessages.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Messages;

using System;
using System.Collections.Generic;
using Workflow.Core.Models;

/// <summary>
/// Message to start a sub-graph execution inside a running workflow~ 🌿✨
/// </summary>
/// <param name="ParentExecutionId">The parent execution ID for history correlation. 🆔.</param>
/// <param name="Definition">The full workflow definition (sub-graph executor scopes using node IDs). 📋.</param>
/// <param name="ScopeNodeIds">
/// The set of node IDs that form this sub-graph.
/// Only these nodes will be executed. Pass empty to run all nodes reachable from entry points.
/// </param>
/// <param name="EntryNodeIds">Node IDs to start executing from (the "first row" of the sub-graph). 🚀.</param>
/// <param name="Inputs">Initial input values for the sub-graph entry nodes. 📥.</param>
/// <param name="SubGraphId">
/// Optional identifier for this sub-graph instance (e.g. loop iteration ID).
/// Persisted on node execution records as metadata so history queries can correlate runs~ 🗂️.
/// </param>
/// <remarks>
/// CopilotNote: SubGraphExecutor is spawned as a child actor and auto-starts on PreStart.
/// This message type is used by loop/parallel modules (2.2.2, 2.2.3) when they ask WorkflowExecutor
/// to spawn a sub-graph on their behalf. Not used by 2.2.0a directly — it's here as the
/// canonical request shape for Phase 2.2.2+~ 💖.
/// </remarks>
public record StartSubGraph(
    Guid ParentExecutionId,
    WorkflowDefinition Definition,
    IReadOnlyList<string> ScopeNodeIds,
    IReadOnlyList<string> EntryNodeIds,
    Dictionary<string, object?> Inputs,
    string? SubGraphId = null);

/// <summary>
/// Sent by SubGraphExecutor to its parent when all sub-graph nodes complete successfully~ 🎉✨
/// </summary>
/// <param name="SubGraphId">The sub-graph identifier (matches <see cref="StartSubGraph.SubGraphId"/>). 🆔.</param>
/// <param name="Outputs">Aggregated outputs from terminal sub-graph nodes. 📤.</param>
public record SubGraphCompleted(
    string? SubGraphId,
    IReadOnlyDictionary<string, object?> Outputs);

/// <summary>
/// Sent by SubGraphExecutor to its parent when the sub-graph fails~ ❌
/// </summary>
/// <param name="SubGraphId">The sub-graph identifier. 🆔.</param>
/// <param name="Error">The exception that caused the failure. 💥.</param>
/// <param name="FailedNodeId">The node ID that triggered the failure, if known. 🧩.</param>
public record SubGraphFailed(
    string? SubGraphId,
    Exception Error,
    string? FailedNodeId = null);

