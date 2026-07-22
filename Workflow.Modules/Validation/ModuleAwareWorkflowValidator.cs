// <copyright file="ModuleAwareWorkflowValidator.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Validation;

using System;
using System.Collections.Generic;
using System.Linq;
using Workflow.Core.Abstractions;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🔌 A workflow validator that extends the base structural checks with
/// module-aware validation: verifying that node <c>ModuleId</c> values exist
/// in the registry, that configured properties are known to the module schema,
/// and that connection port names match the module's declared ports~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: This is Option C from the Phase 1.4.3 deferred decision —
/// a standalone class in <c>Workflow.Modules</c> that wraps the base
/// <see cref="WorkflowValidator"/> via composition. This avoids adding a
/// dependency on <c>Workflow.Modules</c> to <c>Workflow.Core</c>,
/// keeping the layering clean~ 💖.
/// </para>
/// <para>
/// Usage: when a <see cref="IModuleRegistry"/> is available (e.g., at API layer),
/// use this validator instead of the base one. When no registry is available
/// (e.g., unit tests that only care about structure), use the base
/// <see cref="WorkflowValidator"/> directly~ 🎯.
/// </para>
/// <para>
/// Module-aware error codes:
/// <list type="bullet">
/// <item><c>MA001</c> — node ModuleId not found in registry</item>
/// <item><c>MA002</c> — node property key not in module schema</item>
/// <item><c>MA003</c> — connection source port not in module schema outputs</item>
/// <item><c>MA004</c> — connection target port not in module schema inputs</item>
/// </list>
/// </para>
/// </remarks>
public sealed class ModuleAwareWorkflowValidator
{
    private readonly WorkflowValidator inner;
    private readonly IModuleRegistry registry;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleAwareWorkflowValidator"/> class~ 🌸.
    /// </summary>
    /// <param name="registry">
    /// The module registry used to look up module schemas by <c>ModuleId</c>.
    /// Must not be null — if you don't have a registry, use <see cref="WorkflowValidator"/> directly.
    /// </param>
    /// <param name="baseValidator">
    /// Optional base structural validator. If null, a default instance is created~ ✨.
    /// </param>
    public ModuleAwareWorkflowValidator(
        IModuleRegistry registry,
        WorkflowValidator? baseValidator = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        this.registry = registry;
        this.inner = baseValidator ?? new WorkflowValidator();
    }

    /// <summary>
    /// Validates the given workflow definition using both structural checks (from
    /// <see cref="WorkflowValidator"/>) and module-aware checks (registry lookups,
    /// schema port validation)~ 🛡️.
    /// </summary>
    /// <param name="workflow">The workflow definition to validate.</param>
    /// <returns>
    /// A <see cref="ValidationResult"/> combining all structural and module-aware
    /// errors and warnings~ 📋.
    /// </returns>
    public ValidationResult Validate(WorkflowDefinition workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        // 🏗️ Run base structural checks first~
        var baseResult = this.inner.Validate(workflow);

        var errors = new List<ValidationError>(baseResult.Errors);
        var warnings = new List<ValidationWarning>(baseResult.Warnings);

        // 🔍 Build a map of nodeId → module for efficient lookup during port checks~
        var nodeModules = new Dictionary<string, IWorkflowModule>(StringComparer.Ordinal);

        // ✅ Check 1: Every node's ModuleId must exist in the registry~
        foreach (var node in workflow.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.ModuleId))
            {
                // WF004 / base already catches empty node IDs, but ModuleId is separate~
                errors.Add(new ValidationError(
                    "MA001",
                    $"Node '{node.Id}' has an empty or null ModuleId.",
                    node.Id));
                continue;
            }

            var module = this.registry.GetModule(node.ModuleId);
            if (module is null)
            {
                errors.Add(new ValidationError(
                    "MA001",
                    $"Node '{node.Id}' references unknown ModuleId '{node.ModuleId}'. " +
                    $"Make sure the module is registered before validating the workflow.",
                    node.Id,
                    nameof(node.ModuleId)));
            }
            else
            {
                // 🔢 Phase 2.8.2 — When the node pins a module version, that version must exist and be enabled~
                var pinned = ResolvePinnedVersion(node);
                if (pinned is not null)
                {
                    var pinnedModule = this.registry.GetModule(node.ModuleId, pinned);
                    if (pinnedModule is null)
                    {
                        errors.Add(new ValidationError(
                            "MA003",
                            $"Node '{node.Id}' pins module '{node.ModuleId}' version {pinned}, which is not available (missing or disabled).",
                            node.Id,
                            nameof(node.ModuleId)));
                        continue;
                    }

                    module = pinnedModule;
                }

                // 💾 Cache for use in checks 2 & 3 below~
                nodeModules[node.Id] = module;

                // ✅ Check 2: Configured property keys must be known to the module schema~
                ValidateNodeProperties(node, module, errors);
            }
        }

        // ✅ Check 3: Connection port names must match module schema ports~
        ValidateConnectionPorts(workflow, nodeModules, errors);

        return ValidationResult.WithErrorsAndWarnings(errors, warnings);
    }

    /// <summary>
    /// 🔢 Phase 2.8.2 — Reads an optional pinned module version from a node's metadata
    /// (<c>Metadata["moduleVersion"]</c>)~ ✨.
    /// </summary>
    /// <param name="node">The node definition.</param>
    /// <returns>The pinned version, or <c>null</c> when unset/unparseable.</returns>
    private static Version? ResolvePinnedVersion(NodeDefinition node)
    {
        if (node.Metadata is null)
        {
            return null;
        }

        var metadata = node.Metadata.Value;
        if (!metadata.ContainsKey("moduleVersion"))
        {
            return null;
        }

        return Version.TryParse(metadata["moduleVersion"], out var parsed) ? parsed : null;
    }

    /// <summary>
    /// Validates that the property keys configured on a node are all declared
    /// in the module's <see cref="Core.Models.ModuleSchema.Properties"/>~ ⚙️.
    /// </summary>
    /// <param name="node">The node to validate properties on.</param>
    /// <param name="module">The resolved module for this node.</param>
    /// <param name="errors">The error accumulator list.</param>
    private static void ValidateNodeProperties(        NodeDefinition node,
        IWorkflowModule module,
        List<ValidationError> errors)
    {
        // CopilotNote: We only warn on UNKNOWN property keys, not missing required ones —
        // that's the runtime binding system's job (PropertyBinder). Here we just catch
        // typos / stale properties left over after a module update~ 💖
        var knownPropertyNames = module.Schema.Properties
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var configuredKey in node.Properties.Keys)
        {
            // 🎚️ Reserved cross-cutting properties handled by the engine, not the module~
            if (string.Equals(configuredKey, OutputShaping.PropertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!knownPropertyNames.Contains(configuredKey))
            {
                errors.Add(new ValidationError(
                    "MA002",
                    $"Node '{node.Id}' has property '{configuredKey}' which is not declared " +
                    $"in module '{node.ModuleId}' schema. This may be a typo or a stale property " +
                    $"from a previous module version.",
                    node.Id,
                    configuredKey));
            }
        }
    }

    /// <summary>🎚️ Whether a node opts into merged output shaping via the reserved property~.</summary>
    private static bool IsMergedOutputNode(WorkflowDefinition workflow, string nodeId)
        => workflow.Nodes.Find(n => n.Id == nodeId).Match(
            Some: n => n.Properties.Find(OutputShaping.PropertyName).Match(
                Some: v => v.ValueKind == System.Text.Json.JsonValueKind.String && OutputShaping.IsMerged(v.GetString()),
                None: () => false),
            None: () => false);

    /// <summary>
    /// Validates that connection <c>SourcePortName</c> and <c>TargetPortName</c> match
    /// the output/input ports declared in the respective module schemas~ 🔗.
    /// </summary>
    /// <param name="workflow">The workflow being validated.</param>
    /// <param name="nodeModules">Pre-built map of nodeId to resolved module (only valid nodes).</param>
    /// <param name="errors">The error accumulator list.</param>
    private static void ValidateConnectionPorts(
        WorkflowDefinition workflow,
        Dictionary<string, IWorkflowModule> nodeModules,
        List<ValidationError> errors)
    {
        foreach (var connection in workflow.Connections)
        {
            // 🔍 Validate source port — must exist in source module's Outputs~
            if (nodeModules.TryGetValue(connection.SourceNodeId, out var sourceModule) &&
                !string.IsNullOrWhiteSpace(connection.SourcePortName))
            {
                var outputPortNames = sourceModule.Schema.Outputs
                    .Select(p => p.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // 🎚️ Merged-output nodes legitimately expose the single reserved 'output' port~
                var mergedOk = string.Equals(connection.SourcePortName, OutputShaping.MergedPortName, StringComparison.OrdinalIgnoreCase)
                    && IsMergedOutputNode(workflow, connection.SourceNodeId);

                if (!mergedOk && !outputPortNames.Contains(connection.SourcePortName))
                {
                    errors.Add(new ValidationError(
                        "MA003",
                        $"Connection from '{connection.SourceNodeId}' uses output port " +
                        $"'{connection.SourcePortName}' which is not declared in module " +
                        $"'{sourceModule.ModuleId}' schema outputs.",
                        connection.SourceNodeId,
                        connection.SourcePortName));
                }
            }

            // 🔍 Validate target port — must exist in target module's Inputs~
            if (nodeModules.TryGetValue(connection.TargetNodeId, out var targetModule) &&
                !string.IsNullOrWhiteSpace(connection.TargetPortName))
            {
                var inputPortNames = targetModule.Schema.Inputs
                    .Select(p => p.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (!inputPortNames.Contains(connection.TargetPortName))
                {
                    errors.Add(new ValidationError(
                        "MA004",
                        $"Connection to '{connection.TargetNodeId}' uses input port " +
                        $"'{connection.TargetPortName}' which is not declared in module " +
                        $"'{targetModule.ModuleId}' schema inputs.",
                        connection.TargetNodeId,
                        connection.TargetPortName));
                }
            }
        }
    }
}
