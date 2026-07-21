// <copyright file="ConditionalModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Flow;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Workflow.Core.Abstractions;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🔀 Built-in conditional branching module (<c>builtin.condition</c>)~
/// Routes execution to either a <c>true</c> or <c>false</c> downstream branch
/// depending on a boolean condition or an evaluated expression~ ✨💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.2.1 — the first user-visible payoff of the multi-port routing
/// primitive from 2.2.0a. Uses <see cref="ModuleResult.WithActivePorts"/> to activate
/// only one output branch at a time; the other branch nodes are marked Skipped~ 🌸
/// </para>
/// <para>
/// Condition resolution priority (highest → lowest):
/// <list type="number">
///   <item>Input port <c>condition</c> (runtime data from upstream node)</item>
///   <item>Property <c>condition</c> (static configuration)</item>
/// </list>
/// </para>
/// <para>
/// Value coercion order:
/// <list type="number">
///   <item>Direct <c>bool</c> — used as-is.</item>
///   <item>Numeric — <c>0</c> → <c>false</c>, any other value → <c>true</c>.</item>
///   <item>String <c>"true"</c> / <c>"false"</c> / <c>"1"</c> / <c>"0"</c> — parsed directly.</item>
///   <item>Any other string — delegated to <see cref="IExpressionEvaluator"/> if registered in DI.</item>
/// </list>
/// </para>
/// </remarks>
public class ConditionalModule : IWorkflowModule
{
    // ── IWorkflowModule identity ─────────────────────────────────────────────────────

    /// <inheritdoc />
    public string ModuleId => "builtin.condition";

    /// <inheritdoc />
    public string DisplayName => "Conditional Branch";

    /// <inheritdoc />
    public string Category => "Flow Control";

    /// <inheritdoc />
    public string Description => "Routes to the true or false branch based on a boolean condition or expression~ 🔀✨";

    /// <inheritdoc />
    public string Icon => "🔀";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    /// <remarks>
    /// CopilotNote: The schema declares THREE output ports:
    /// <list type="bullet">
    ///   <item><c>true</c>  — activation-only port; no data payload. Engine fires if condition == true.</item>
    ///   <item><c>false</c> — activation-only port; no data payload. Engine fires if condition == false.</item>
    ///   <item><c>result</c> — carries the evaluated boolean value for diagnostics / logging.</item>
    /// </list>
    /// Because all three are declared, <c>ValidateConnectionPorts</c> will reject any connection
    /// using an undeclared port name at workflow load time~ 🛡️
    /// </remarks>
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition(
                Name: "condition",
                DisplayName: "Condition",
                DataType: typeof(object),
                Description: "Boolean or string expression to evaluate. Overrides the condition property when connected~ 🔗",
                IsRequired: false)),
        Outputs: Arr.create(
            new PortDefinition(
                Name: "true",
                DisplayName: "True Branch",
                DataType: typeof(object),
                Description: "Activates when the condition evaluates to true. No data payload~ ✅",
                IsRequired: false),
            new PortDefinition(
                Name: "false",
                DisplayName: "False Branch",
                DataType: typeof(object),
                Description: "Activates when the condition evaluates to false. No data payload~ ❌",
                IsRequired: false),
            new PortDefinition(
                Name: "result",
                DisplayName: "Result",
                DataType: typeof(bool),
                Description: "The evaluated boolean value, for diagnostics~ 📊",
                IsRequired: false)),
        Properties: Arr.create(
            new ModulePropertyDefinition(
                Name: "condition",
                DisplayName: "Condition",
                DataType: typeof(string),
                Description: "Boolean value or expression string (e.g. 'true', 'x > 5'). Overridden by connected input port~ 💬",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Expression)));

    // ── Execution ────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // 1. Resolve the raw condition value — input port takes priority over property~ 🔗
        object? conditionValue = null;
        if (context.Inputs.TryGetValue("condition", out var inputVal) && inputVal is not null)
        {
            conditionValue = inputVal;
        }
        else if (context.Properties.TryGetValue("condition", out var propVal) && propVal is not null)
        {
            conditionValue = propVal;
        }

        // 2. Guard: condition must be present~ 🛡️
        if (conditionValue is null)
        {
            context.Logger.LogWarning("🔀 ConditionalModule: no condition value provided~ 💔");
            return ModuleResult.Fail("Condition is required but was null or not provided~ 💔");
        }

        // 3. Coerce to bool~ 🧩
        bool result;
        switch (conditionValue)
        {
            case bool b:
                result = b;
                context.Logger.LogDebug("🔀 Condition (direct bool): {Value}", result);
                break;

            case int i:
                result = i != 0;
                context.Logger.LogDebug("🔀 Condition (int coerce): {Raw} → {Value}", i, result);
                break;

            case long l:
                result = l != 0L;
                context.Logger.LogDebug("🔀 Condition (long coerce): {Raw} → {Value}", l, result);
                break;

            case double d:
                result = Math.Abs(d) > double.Epsilon;
                context.Logger.LogDebug("🔀 Condition (double coerce): {Raw} → {Value}", d, result);
                break;

            case string s:
                // Try simple parse first (covers "true"/"false"/"1"/"0")~ 🏃
                if (TryCoerceString(s, out var strResult))
                {
                    result = strResult;
                    context.Logger.LogDebug("🔀 Condition (string coerce): '{Raw}' → {Value}", s, result);
                }
                else
                {
                    // Delegate to expression evaluator~ 🧮
                    var evaluator = context.Services.GetService<IExpressionEvaluator>();
                    if (evaluator is null)
                    {
                        context.Logger.LogWarning(
                            "🔀 ConditionalModule: cannot evaluate expression '{Expr}' — no IExpressionEvaluator registered~ 💔", s);
                        return ModuleResult.Fail(
                            $"Cannot evaluate expression '{s}': no IExpressionEvaluator is registered. " +
                            "Register an implementation (e.g. JintExpressionEvaluator) in the DI container~ 💔");
                    }

                    try
                    {
                        result = await evaluator.EvaluateAsync<bool>(s, context.Variables, cancellationToken);
                        context.Logger.LogDebug("🔀 Condition (expression): '{Expr}' → {Value}", s, result);
                    }
                    catch (ExpressionParseException ex)
                    {
                        context.Logger.LogWarning("🔀 ConditionalModule: parse error in '{Expr}': {Reason}", s, ex.Reason);
                        return ModuleResult.Fail($"Expression parse error: {ex.Reason}~ 💔", ex);
                    }
                    catch (ExpressionRuntimeException ex)
                    {
                        context.Logger.LogWarning("🔀 ConditionalModule: runtime error in '{Expr}': {Reason}", s, ex.Reason);
                        return ModuleResult.Fail($"Expression runtime error: {ex.Reason}~ 💔", ex);
                    }
                }

                break;

            default:
                context.Logger.LogWarning(
                    "🔀 ConditionalModule: cannot coerce {Type} to bool~ 💔",
                    conditionValue.GetType().Name);
                return ModuleResult.Fail(
                    $"Cannot coerce condition value of type '{conditionValue.GetType().Name}' to bool~ 💔");
        }

        // 4. Build outputs and activate the appropriate port~ 🎯
        var activePort = result ? "true" : "false";
        var outputs = new Dictionary<string, object?>
        {
            ["result"] = result,
        };

        context.Logger.LogInformation("🔀 Condition evaluated: {Result} → activating port '{Port}'", result, activePort);

        return ModuleResult.WithActivePorts(outputs, new[] { activePort });
    }

    // ── Private helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Tries to coerce a string to bool without invoking the expression evaluator~
    /// Handles: "true"/"false" (case-insensitive), "1"/"0", "yes"/"no", "on"/"off"~ 🌸
    /// </summary>
    private static bool TryCoerceString(string value, out bool result)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "true":
            case "1":
            case "yes":
            case "on":
                result = true;
                return true;

            case "false":
            case "0":
            case "no":
            case "off":
                result = false;
                return true;

            default:
                result = false;
                return false;
        }
    }
}

