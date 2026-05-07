// <copyright file="SwitchModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Flow;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🔢 Built-in multi-way switch module (<c>builtin.switch</c>)~
/// Compares an input value against an ordered list of cases and activates
/// the matching case's output port. Falls back to a default port when no case matches~ ✨💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.2.1. Output ports are DYNAMIC (one per case + optional "default"),
/// so the schema declares an empty <c>Outputs</c> collection. This causes
/// <c>ValidateConnectionPorts</c> to skip port-name validation for this module — the engine
/// trusts that the workflow author's connection port names match the configured cases~ 🎗️
/// </para>
/// <para>
/// <b>Cases format</b> (property <c>cases</c>): a JSON array of objects, each with a
/// <c>match</c> field (the value to compare) and a <c>port</c> field (the output port to activate):
/// <code>
/// [
///   { "match": "cat", "port": "case_cat" },
///   { "match": "dog", "port": "case_dog" }
/// ]
/// </code>
/// Matching is case-insensitive string comparison by default. First match wins~ 🌟
/// </para>
/// <para>
/// <b>Value resolution</b> — input port takes priority over property:
/// <list type="number">
///   <item>Input port <c>value</c> — runtime data from upstream node.</item>
///   <item>Property <c>value</c> — static string configuration.</item>
/// </list>
/// </para>
/// </remarks>
public class SwitchModule : IWorkflowModule
{
    // ── IWorkflowModule identity ─────────────────────────────────────────────────────

    /// <inheritdoc />
    public string ModuleId => "builtin.switch";

    /// <inheritdoc />
    public string DisplayName => "Switch";

    /// <inheritdoc />
    public string Category => "Flow Control";

    /// <inheritdoc />
    public string Description => "Routes execution to one of many branches based on a matching value~ 🔢✨";

    /// <inheritdoc />
    public string Icon => "🔢";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    /// <remarks>
    /// CopilotNote: Outputs are intentionally EMPTY — ports are dynamic (one per case + "default").
    /// <c>ValidateConnectionPorts</c> skips validation for modules with no declared outputs~ 🎗️
    /// </remarks>
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition(
                Name: "value",
                DisplayName: "Value",
                DataType: typeof(object),
                Description: "The value to match against cases. Overrides the value property when connected~ 🔗",
                IsRequired: false)),
        Outputs: Arr<PortDefinition>.Empty,
        Properties: Arr.create(
            new ModulePropertyDefinition(
                Name: "value",
                DisplayName: "Value",
                DataType: typeof(string),
                Description: "Static value to match. Overridden by connected input port~ 💬",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text),
            new ModulePropertyDefinition(
                Name: "cases",
                DisplayName: "Cases",
                DataType: typeof(string),
                Description: "JSON array of {match, port} objects. First match wins~ 📋",
                IsRequired: true,
                DefaultValue: null,
                EditorType: PropertyEditorType.Json),
            new ModulePropertyDefinition(
                Name: "defaultPort",
                DisplayName: "Default Port",
                DataType: typeof(string),
                Description: "Output port to activate when no case matches. Leave empty to fail on no-match~ 🎯",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text),
            new ModulePropertyDefinition(
                Name: "caseSensitive",
                DisplayName: "Case Sensitive",
                DataType: typeof(bool),
                Description: "Whether string comparison is case-sensitive (default false)~ 🔡",
                IsRequired: false,
                DefaultValue: false,
                EditorType: PropertyEditorType.Boolean)));

    // ── Configuration validation ─────────────────────────────────────────────────────

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        if (!configuration.TryGetValue("cases", out var casesRaw) || casesRaw is null)
        {
            return ValidationResult.Failure(
                new ValidationError("MISSING_CASES", "The 'cases' property is required~ 💔", "cases"));
        }

        var cases = ParseCases(casesRaw, out var parseError);
        if (parseError is not null)
        {
            return ValidationResult.Failure(
                new ValidationError("INVALID_CASES", $"Cannot parse 'cases': {parseError}~ 💔", "cases"));
        }

        if (cases is null || cases.Count == 0)
        {
            return ValidationResult.Failure(
                new ValidationError("EMPTY_CASES", "The 'cases' array must contain at least one entry~ 💔", "cases"));
        }

        return ValidationResult.Success();
    }

    // ── Execution ────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // 1. Resolve the value to match — input port takes priority over property~ 🔗
        object? valueRaw = null;
        if (context.Inputs.TryGetValue("value", out var inputVal) && inputVal is not null)
        {
            valueRaw = inputVal;
        }
        else if (context.Properties.TryGetValue("value", out var propVal) && propVal is not null)
        {
            valueRaw = propVal;
        }

        var valueStr = ConvertToString(valueRaw);

        // 2. Parse case sensitivity setting~ 🔡
        var caseSensitive = false;
        if (context.Properties.TryGetValue("caseSensitive", out var csRaw) && csRaw is bool csBool)
        {
            caseSensitive = csBool;
        }

        // 3. Parse cases~ 📋
        if (!context.Properties.TryGetValue("cases", out var casesRaw) || casesRaw is null)
        {
            return Task.FromResult(ModuleResult.Fail("The 'cases' property is required~ 💔"));
        }

        var cases = ParseCases(casesRaw, out var parseError);
        if (parseError is not null)
        {
            return Task.FromResult(ModuleResult.Fail($"Cannot parse 'cases': {parseError}~ 💔"));
        }

        if (cases is null || cases.Count == 0)
        {
            return Task.FromResult(ModuleResult.Fail("The 'cases' array must contain at least one entry~ 💔"));
        }

        // 4. Find first matching case~ 🔍
        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        string? matchedPort = null;
        string? matchedValue = null;
        foreach (var c in cases)
        {
            if (!c.TryGetValue("match", out var matchObj) || matchObj is null)
            {
                continue; // Skip malformed case entry~ ⚠️
            }

            var matchStr = ConvertToString(matchObj);
            if (string.Equals(valueStr, matchStr, comparison))
            {
                if (c.TryGetValue("port", out var portObj) && portObj is string port && !string.IsNullOrWhiteSpace(port))
                {
                    matchedPort = port;
                    matchedValue = matchStr;
                    break;
                }
            }
        }

        // 5. Fall back to defaultPort or fail~ 🎯
        if (matchedPort is null)
        {
            var defaultPort = context.Properties.TryGetValue("defaultPort", out var dpRaw) && dpRaw is string dp
                ? dp.Trim()
                : string.Empty;

            if (string.IsNullOrWhiteSpace(defaultPort))
            {
                context.Logger.LogWarning(
                    "🔢 SwitchModule: no case matched value '{Value}' and no defaultPort is configured~ 💔",
                    valueStr);
                return Task.FromResult(ModuleResult.Fail(
                    $"No case matched value '{valueStr}' and no defaultPort is configured~ 💔"));
            }

            matchedPort = defaultPort;
            matchedValue = null; // fell through to default
        }

        // 6. Activate the matched port~ 🎉
        var outputs = new Dictionary<string, object?>
        {
            ["matchedPort"] = matchedPort,
            ["matchedValue"] = matchedValue,
            ["value"] = valueStr,
        };

        context.Logger.LogInformation(
            "🔢 Switch: value='{Value}' matched port='{Port}'",
            valueStr, matchedPort);

        return Task.FromResult(ModuleResult.WithActivePorts(outputs, new[] { matchedPort }));
    }

    // ── Private helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses the cases property into a list of case dictionaries~
    /// Handles both pre-parsed <c>List&lt;object?&gt;</c> (from ConvertJsonElement) and JSON strings~ 🔧
    /// </summary>
    private static List<Dictionary<string, object?>>? ParseCases(object casesRaw, out string? error)
    {
        error = null;

        // If it's already a list (ConvertJsonElement output), map each item~ 📋
        if (casesRaw is List<object?> list)
        {
            var result = new List<Dictionary<string, object?>>();
            foreach (var item in list)
            {
                if (item is Dictionary<string, object?> dict)
                {
                    result.Add(dict);
                }
                else
                {
                    error = $"Each case entry must be an object (got {item?.GetType().Name ?? "null"})";
                    return null;
                }
            }

            return result;
        }

        // If it's a JSON string, deserialize it~ 🌱
        if (casesRaw is string json)
        {
            try
            {
                var deserialized = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json);
                if (deserialized is null)
                {
                    error = "deserialized to null";
                    return null;
                }

                // Convert JsonElements to raw objects for uniform handling~ 🔄
                return deserialized.Select(d => d.ToDictionary(
                    kv => kv.Key,
                    kv => ConvertJsonElement(kv.Value))).ToList();
            }
            catch (JsonException ex)
            {
                error = ex.Message;
                return null;
            }
        }

        error = $"Expected JSON string or list, got {casesRaw.GetType().Name}";
        return null;
    }

    /// <summary>
    /// Converts a value to its string representation for comparison~ 🔄
    /// </summary>
    private static string ConvertToString(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string s => s,
            bool b => b.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(), // "true" or "false"
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    /// <summary>
    /// Converts a <see cref="JsonElement"/> to a raw .NET object for case matching~ 🔄
    /// </summary>
    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? (object)l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString(),
        };
    }
}

