// <copyright file="ModuleResolution.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules;

using System;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🔢 The one way to find the module a node runs: its pinned version
/// (<c>Metadata["moduleVersion"]</c>) when set, otherwise the latest enabled version~ ✨.
/// </summary>
/// <remarks>
/// §9.4 (streaming): every place that resolves a node's module — batch execution, streaming regions,
/// region detection, save-time and run-time validation — goes through here, so a pin can't be
/// honoured in one place and ignored in another again (streaming regions used to run the latest
/// version of a pinned node).
/// </remarks>
public static class ModuleResolution
{
    /// <summary>The node metadata key that pins a module version.</summary>
    public const string PinnedVersionKey = "moduleVersion";

    /// <summary>
    /// Reads the node's pinned module version. Null when unset or unparseable (then the latest
    /// enabled version is used).
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The pinned version, or null.</returns>
    public static Version? PinnedVersion(NodeDefinition node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Metadata is not { } metadata || !metadata.ContainsKey(PinnedVersionKey))
        {
            return null;
        }

        return Version.TryParse(metadata[PinnedVersionKey], out var parsed) ? parsed : null;
    }

    /// <summary>Resolves the module a node runs, honouring a version pin.</summary>
    /// <param name="registry">The module registry (null resolves nothing).</param>
    /// <param name="node">The node.</param>
    /// <returns>The module, or why it couldn't be resolved.</returns>
    public static ModuleResolutionResult Resolve(IModuleRegistry? registry, NodeDefinition node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var pinned = PinnedVersion(node);
        if (registry is null)
        {
            return new ModuleResolutionResult(null, pinned, $"Module '{node.ModuleId}' is not registered.");
        }

        if (pinned is not null)
        {
            var module = registry.GetModule(node.ModuleId, pinned);
            if (module is not null)
            {
                return new ModuleResolutionResult(module, pinned, null);
            }

            // The module exists but the pinned version is missing/disabled — say so, don't fall back~
            return registry.HasModule(node.ModuleId)
                ? new ModuleResolutionResult(null, pinned, $"Module '{node.ModuleId}' version {pinned} is not available (missing or disabled).")
                : new ModuleResolutionResult(null, pinned, $"Module '{node.ModuleId}' is not registered.");
        }

        var latest = registry.GetModule(node.ModuleId);
        return latest is not null
            ? new ModuleResolutionResult(latest, null, null)
            : new ModuleResolutionResult(null, null, $"Module '{node.ModuleId}' is not registered.");
    }
}

/// <summary>The outcome of <see cref="ModuleResolution.Resolve"/>.</summary>
/// <param name="Module">The resolved module, or null.</param>
/// <param name="PinnedVersion">The node's pinned version, if any.</param>
/// <param name="Error">Why resolution failed (null on success).</param>
public sealed record ModuleResolutionResult(IWorkflowModule? Module, Version? PinnedVersion, string? Error);
