// <copyright file="RegionErrorHandling.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Streaming;

using System;
using System.Collections.Generic;
using System.Linq;
using Workflow.Core.Models;

/// <summary>
/// 🧯 Stage 7.8 — error handling for a stage of a streaming region. A failure is blamed on the
/// stage that failed, and that stage's own error handling applies — unless it opts into the
/// region's (its source's) with <see cref="UseRegionErrorHandlingKey"/>. After continue / an error
/// handler, what follows the region runs unless the stage sets <see cref="SkipWhatFollowsKey"/>.
/// </summary>
public static class RegionErrorHandling
{
    /// <summary>Node metadata: <c>"true"</c> = use the region source's error handling instead of this stage's.</summary>
    public const string UseRegionErrorHandlingKey = "useRegionErrorHandling";

    /// <summary>Node metadata: <c>"true"</c> = after continue / an error handler here, skip what follows the region.</summary>
    public const string SkipWhatFollowsKey = "regionSkipWhatFollows";

    /// <summary>The history metadata key carrying why a stage was skipped.</summary>
    public const string SkipReasonKey = "skipReason";

    /// <summary>Whether a node's metadata turns a flag on.</summary>
    /// <param name="node">The node.</param>
    /// <param name="key">The metadata key.</param>
    /// <returns>True when the key is <c>"true"</c>.</returns>
    public static bool Flag(NodeDefinition? node, string key)
        => node?.Metadata is { } metadata
           && metadata.Find(key).IfNoneUnsafe((string?)null) is { } value
           && string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The definition whose error handling applies to a node: itself, or — for a stage that opted in —
    /// the node with its region source's <c>ErrorHandling</c> and <c>RetryPolicy</c>.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="regionSourceOf">The node's region source id, or null when it isn't in a region.</param>
    /// <param name="find">Finds a node by id.</param>
    /// <returns>The node to read error handling from.</returns>
    public static NodeDefinition? Effective(NodeDefinition? node, Func<string, string?> regionSourceOf, Func<string, NodeDefinition?> find)
    {
        if (node is null || !Flag(node, UseRegionErrorHandlingKey) || regionSourceOf(node.Id) is not { } sourceId
            || string.Equals(sourceId, node.Id, StringComparison.Ordinal) || find(sourceId) is not { } source)
        {
            return node;
        }

        return node with { ErrorHandling = source.ErrorHandling, RetryPolicy = source.RetryPolicy };
    }

    /// <summary>The reason recorded on a stage skipped because an earlier stage failed.</summary>
    /// <param name="failedNodeId">The stage that failed.</param>
    /// <returns>The reason.</returns>
    public static string SkipReason(string failedNodeId) => $"skipped: upstream stage '{failedNodeId}' failed";

    /// <summary>Splits a region's chain around the failed stage.</summary>
    /// <param name="order">The region's stages, source first.</param>
    /// <param name="failedNodeId">The failed stage.</param>
    /// <returns>The stages before it and after it.</returns>
    public static (IReadOnlyList<string> Before, IReadOnlyList<string> After) Around(IReadOnlyList<string> order, string failedNodeId)
    {
        var index = order.ToList().IndexOf(failedNodeId);
        return index < 0
            ? (Array.Empty<string>(), order.Where(n => !string.Equals(n, failedNodeId, StringComparison.Ordinal)).ToList())
            : (order.Take(index).ToList(), order.Skip(index + 1).ToList());
    }
}
