// <copyright file="RegionTimeouts.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;
using System.Globalization;
using System.Linq;

/// <summary>
/// ⏱️ Stage 7.5 — a streaming region's time limits, kept in its <b>source</b> node's metadata (the
/// engine's <c>StreamingTimeoutOptions</c> keys). Absent = the server's setting
/// (<c>Streaming:RegionStallTimeoutMs</c> / <c>Streaming:RegionTimeoutMs</c>, off by default);
/// <c>0</c> = off for this region. The designer edits them in seconds.
/// </summary>
public static class RegionTimeouts
{
    /// <summary>The stall limit's metadata key (milliseconds).</summary>
    public const string StallKey = "regionStallTimeoutMs";

    /// <summary>The overall limit's metadata key (milliseconds).</summary>
    public const string TotalKey = "regionTimeoutMs";

    /// <summary>Whether a node starts a streaming region (a member with no incoming streaming edge).</summary>
    /// <param name="doc">The document.</param>
    /// <param name="node">The node.</param>
    /// <returns>True for a region's source node.</returns>
    public static bool IsRegionSource(DesignerDocument doc, DesignerNode node)
        => StreamGraph.RegionIndexByNode(doc).ContainsKey(node.Id)
           && !doc.Connections.Any(c => string.Equals(c.TargetNodeId, node.Id, StringComparison.Ordinal) && StreamGraph.IsStreamingEdge(doc, c));

    /// <summary>A limit as seconds for the editor ("" = not set).</summary>
    /// <param name="node">The source node.</param>
    /// <param name="key">The metadata key.</param>
    /// <returns>The seconds text.</returns>
    public static string SecondsText(DesignerNode node, string key)
        => node.Metadata.TryGetValue(key, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms)
            ? (ms / 1000.0).ToString("0.###", CultureInfo.InvariantCulture)
            : string.Empty;

    /// <summary>Converts editor seconds to the stored milliseconds (null = remove: use the server's setting).</summary>
    /// <param name="seconds">The text entered.</param>
    /// <returns>The milliseconds text, or null.</returns>
    public static string? ToStored(string? seconds)
    {
        if (string.IsNullOrWhiteSpace(seconds)
            || !double.TryParse(seconds.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || value < 0)
        {
            return null;
        }

        return ((long)Math.Round(value * 1000)).ToString(CultureInfo.InvariantCulture);
    }
}
