// <copyright file="RegionStageErrors.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;
using System.Linq;

/// <summary>
/// 🧯 Stage 7.8 — error handling for a stage of a streaming region (the engine's
/// <c>RegionErrorHandling</c> keys). A failure is blamed on the stage that failed and its own
/// error handling applies, unless it opts into the region's (its source's). After continue / an
/// error handler, what follows the region runs, unless the stage says to skip it.
/// </summary>
public static class RegionStageErrors
{
    /// <summary>Node metadata: <c>"true"</c> = use the region source's error handling.</summary>
    public const string UseRegionKey = "useRegionErrorHandling";

    /// <summary>Node metadata: <c>"true"</c> = after continue / an error handler, skip what follows the region.</summary>
    public const string SkipWhatFollowsKey = "regionSkipWhatFollows";

    /// <summary>A node's place in a streaming region.</summary>
    /// <param name="SourceId">The region's source node.</param>
    /// <param name="SourceName">The source's display name.</param>
    /// <param name="IsSource">Whether the node is the source.</param>
    /// <param name="PerItem">Whether the stage is per-item (retries an item in place).</param>
    public sealed record Info(string SourceId, string SourceName, bool IsSource, bool PerItem);

    /// <summary>The node's region info, or null when it isn't in a streaming region.</summary>
    /// <param name="doc">The document.</param>
    /// <param name="node">The node.</param>
    /// <returns>The info, or null.</returns>
    public static Info? For(DesignerDocument doc, DesignerNode node)
    {
        var region = StreamGraph.Regions(doc).FirstOrDefault(r => r.NodeIds.Contains(node.Id, StringComparer.Ordinal));
        if (region is null)
        {
            return null;
        }

        var source = region.NodeIds
            .Select(doc.FindNode)
            .FirstOrDefault(n => n is not null && RegionTimeouts.IsRegionSource(doc, n));
        if (source is null)
        {
            return null;
        }

        return new Info(
            source.Id,
            source.Name,
            string.Equals(source.Id, node.Id, StringComparison.Ordinal),
            string.Equals(node.Schema?.StreamShape, "perItem", StringComparison.Ordinal));
    }

    /// <summary>Whether a metadata flag is on.</summary>
    /// <param name="node">The node.</param>
    /// <param name="key">The key.</param>
    /// <returns>True when <c>"true"</c>.</returns>
    public static bool Flag(DesignerNode node, string key)
        => node.Metadata.TryGetValue(key, out var value) && string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>What retry does for this stage.</summary>
    /// <param name="info">The stage's region info.</param>
    /// <returns>The help text.</returns>
    public static string RetryHelp(Info info)
        => info.PerItem
            ? "This per-item stage retries the failing item in place (the stream keeps flowing), then its 'on item error' applies."
            : $"A stage can't re-run on its own in a stream, so a retry re-runs the whole region from '{info.SourceName}'.";
}
