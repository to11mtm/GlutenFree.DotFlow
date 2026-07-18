// <copyright file="ValidateDataModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Transform;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Json.Schema;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Transform.Internal;
using Workflow.Modules.Internal;

/// <summary>
/// ✅ Built-in Validate Data module (<c>builtin.transform.validate</c>) — declarative per-field rules
/// or a JSON Schema, with an expression escape hatch (D9)~ ✨.
/// </summary>
public sealed class ValidateDataModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.transform.validate";

    /// <inheritdoc />
    public string DisplayName => "Validate Data";

    /// <inheritdoc />
    public string Category => "Transformation";

    /// <inheritdoc />
    public string Description => "Validates records against declarative rules or a JSON Schema~ ✅✨";

    /// <inheritdoc />
    public string Icon => "✅";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition("data", "Data", typeof(object), "Record or array of records to validate~ 📥", false)),
        Outputs: Arr.create(
            new PortDefinition("isValid", "Is Valid", typeof(bool), "Whether all records passed~ ✅", false),
            new PortDefinition("errors", "Errors", typeof(object), "Validation errors~ ❌", false),
            new PortDefinition("validItems", "Valid Items", typeof(object), "Records that passed (array input)~ ✅", false),
            new PortDefinition("invalidItems", "Invalid Items", typeof(object), "Records that failed (array input)~ ❌", false),
            new PortDefinition("success", "Success", typeof(bool), "Whether the module ran~ ✅", false)),
        Properties: Arr.create(
            new ModulePropertyDefinition("data", "Data", typeof(object), "Data when not connected via port~ 📥", false, null, PropertyEditorType.Json),
            new ModulePropertyDefinition("rules", "Rules", typeof(object), "Array of { field, rule, value?, message? }~ 📋", false, null, PropertyEditorType.Json),
            new ModulePropertyDefinition("schema", "JSON Schema", typeof(object), "A JSON Schema (mutually exclusive with rules)~ 📐", false, null, PropertyEditorType.Json),
            new ModulePropertyDefinition("failOnInvalid", "Fail On Invalid", typeof(bool), "Return a module failure when invalid~ 💔", false, false, PropertyEditorType.Boolean),
            new ModulePropertyDefinition("language", "Expression Language", typeof(string), "js (default) or csharp (custom rules)~ 🧮", false, "js", PropertyEditorType.Text)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        var hasRules = configuration.TryGetValue("rules", out var r) && r is not null;
        var hasSchema = configuration.TryGetValue("schema", out var s) && s is not null;

        if (hasRules && hasSchema)
        {
            return ValidationResult.Failure(new ValidationError("RULES_XOR_SCHEMA", "set either rules or schema, not both~ 💔"));
        }

        if (!hasRules && !hasSchema)
        {
            return ValidationResult.Failure(new ValidationError("RULES_OR_SCHEMA_REQUIRED", "one of rules or schema is required~ 💔"));
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var hasRules = context.Properties.TryGetValue("rules", out var rulesRaw) && rulesRaw is not null;
        var hasSchema = context.Properties.TryGetValue("schema", out var schemaRaw) && schemaRaw is not null;

        if (hasRules == hasSchema)
        {
            return ModuleResult.Fail("✅ Set exactly one of rules or schema~ 💔");
        }

        var raw = TransformSupport.ReadData(context, "data");
        var normalized = TransformDataNormalizer.Normalize(raw);
        var isArray = normalized is IReadOnlyList<object?>;
        var records = normalized switch
        {
            IReadOnlyList<object?> list => list,
            null => new List<object?>(),
            _ => new List<object?> { normalized },
        };

        var failOnInvalid = TransformSupport.GetBool(context.Properties, "failOnInvalid");

        var sw = Stopwatch.StartNew();
        try
        {
            var allErrors = new List<object?>();
            var validItems = new List<object?>();
            var invalidItems = new List<object?>();

            JsonSchema? jsonSchema = hasSchema ? BuildSchema(schemaRaw) : null;
            var rules = hasRules ? ParseRules(rulesRaw) : null;
            ItemExpressionEvaluator? evaluator = null;
            if (hasRules)
            {
                if (!ItemExpressionEvaluator.TryResolve(context, TransformSupport.GetString(context.Properties, "language"), out evaluator, out var evalFail))
                {
                    return evalFail!;
                }
            }

            var index = 0;
            foreach (var record in records)
            {
                var itemErrors = jsonSchema is not null
                    ? ValidateSchema(jsonSchema, record, index)
                    : await ValidateRules(context, evaluator!, rules!, record, index, isArray, cancellationToken).ConfigureAwait(false);

                if (itemErrors.Count == 0)
                {
                    validItems.Add(record);
                }
                else
                {
                    invalidItems.Add(record);
                    allErrors.AddRange(itemErrors);
                }

                index++;
            }

            sw.Stop();
            var isValid = allErrors.Count == 0;

            if (!isValid && failOnInvalid)
            {
                return ModuleResult.Fail($"✅ Validation failed with {allErrors.Count} error(s)~ 💔");
            }

            var outputs = new Dictionary<string, object?>
            {
                ["isValid"] = isValid,
                ["errors"] = allErrors,
                ["success"] = true,
            };

            if (isArray)
            {
                outputs["validItems"] = validItems;
                outputs["invalidItems"] = invalidItems;
            }

            return ModuleResult.Ok(outputs, ExecutionMetrics.FromDuration(sw.Elapsed));
        }
        catch (TransformModuleException ex)
        {
            return ModuleResult.Fail($"✅ Validation error: {ex.Message}~ 💔", ex);
        }
    }

    private static JsonSchema BuildSchema(object? schemaRaw)
    {
        var node = System.Text.Json.JsonSerializer.SerializeToNode(TransformDataNormalizer.Normalize(schemaRaw));
        return JsonSchema.FromText(node!.ToJsonString());
    }

    private static List<object?> ValidateSchema(JsonSchema schema, object? record, int index)
    {
        var node = System.Text.Json.JsonSerializer.SerializeToNode(record);
        var evaluation = schema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List });
        var errors = new List<object?>();

        if (!evaluation.IsValid)
        {
            CollectSchemaErrors(evaluation, index, errors);
            if (errors.Count == 0)
            {
                errors.Add(new Dictionary<string, object?> { ["index"] = index, ["field"] = string.Empty, ["rule"] = "schema", ["message"] = "does not match schema" });
            }
        }

        return errors;
    }

    private static void CollectSchemaErrors(EvaluationResults results, int index, List<object?> errors)
    {
        if (results.HasErrors && results.Errors is not null)
        {
            foreach (var kvp in results.Errors)
            {
                errors.Add(new Dictionary<string, object?>
                {
                    ["index"] = index,
                    ["field"] = results.InstanceLocation.ToString(),
                    ["rule"] = kvp.Key,
                    ["message"] = kvp.Value,
                });
            }
        }

        foreach (var detail in results.Details)
        {
            CollectSchemaErrors(detail, index, errors);
        }
    }

    private static List<IReadOnlyDictionary<string, object?>> ParseRules(object? rulesRaw)
    {
        var normalized = TransformDataNormalizer.Normalize(rulesRaw);
        var result = new List<IReadOnlyDictionary<string, object?>>();
        if (normalized is IReadOnlyList<object?> list)
        {
            foreach (var item in list)
            {
                if (item is IReadOnlyDictionary<string, object?> rule)
                {
                    result.Add(rule);
                }
            }
        }

        return result;
    }

    private async Task<List<object?>> ValidateRules(
        ModuleExecutionContext context,
        ItemExpressionEvaluator evaluator,
        List<IReadOnlyDictionary<string, object?>> rules,
        object? record,
        int index,
        bool isArray,
        CancellationToken ct)
    {
        var errors = new List<object?>();

        foreach (var rule in rules)
        {
            var field = rule.TryGetValue("field", out var f) ? f?.ToString() : null;
            var ruleKind = (rule.TryGetValue("rule", out var rk) ? rk?.ToString() : null)?.ToLowerInvariant();
            if (ruleKind is null)
            {
                continue;
            }

            var value = field is null ? record : DotPath.Resolve(record, field, out _);
            var ruleValue = rule.TryGetValue("value", out var rv) ? rv : null;
            var (ok, defaultMessage) = await this.CheckRule(context, evaluator, ruleKind, value, ruleValue, record, index, ct).ConfigureAwait(false);

            if (!ok)
            {
                var message = rule.TryGetValue("message", out var m) && m is not null ? m.ToString() : defaultMessage;
                var err = new Dictionary<string, object?> { ["field"] = field ?? string.Empty, ["rule"] = ruleKind, ["message"] = message };
                if (isArray)
                {
                    err["index"] = index;
                }

                errors.Add(err);
            }
        }

        return errors;
    }

    private async Task<(bool Ok, string Message)> CheckRule(
        ModuleExecutionContext context,
        ItemExpressionEvaluator evaluator,
        string rule,
        object? value,
        object? ruleValue,
        object? record,
        int index,
        CancellationToken ct)
    {
        switch (rule)
        {
            case "required":
                return (value is not null && !(value is string s0 && s0.Length == 0), "is required");
            case "type":
                return (CheckType(value, ruleValue?.ToString()), $"must be of type {ruleValue}");
            case "minlength":
                return (value?.ToString()?.Length >= ToInt(ruleValue), $"must be at least {ruleValue} characters");
            case "maxlength":
                return (value?.ToString()?.Length <= ToInt(ruleValue), $"must be at most {ruleValue} characters");
            case "min":
                return (TransformComparer.TryToDouble(value, out var dn) && dn >= ToDouble(ruleValue), $"must be >= {ruleValue}");
            case "max":
                return (TransformComparer.TryToDouble(value, out var dx) && dx <= ToDouble(ruleValue), $"must be <= {ruleValue}");
            case "pattern":
                return (value is not null && SafeRegex.Create(ruleValue?.ToString() ?? string.Empty).IsMatch(value.ToString()!), $"must match pattern {ruleValue}");
            case "email":
                return (value is not null && SafeRegex.Create(@"^[^@\s]+@[^@\s]+\.[^@\s]+$").IsMatch(value.ToString()!), "must be a valid email");
            case "url":
                return (value is not null && Uri.TryCreate(value.ToString(), UriKind.Absolute, out _), "must be a valid URL");
            case "enum":
                return (CheckEnum(value, ruleValue), $"must be one of the allowed values");
            case "minitems":
                return (value is IReadOnlyList<object?> li && li.Count >= ToInt(ruleValue), $"must have at least {ruleValue} items");
            case "maxitems":
                return (value is IReadOnlyList<object?> lx && lx.Count <= ToInt(ruleValue), $"must have at most {ruleValue} items");
            case "custom":
                var scope = ItemExpressionEvaluator.Scope(context, record, index, new Dictionary<string, object?> { ["value"] = value });
                var passed = await evaluator.EvalPredicateAsync(ruleValue?.ToString() ?? "true", scope, index, ct).ConfigureAwait(false);
                return (passed, "failed custom validation");
            default:
                return (true, string.Empty);
        }
    }

    private static bool CheckType(object? value, string? type)
    {
        if (value is null)
        {
            return true; // presence is 'required's job
        }

        return type?.ToLowerInvariant() switch
        {
            "string" => value is string,
            "number" => TransformComparer.TryToDouble(value, out _) && value is not string,
            "bool" or "boolean" => value is bool,
            "array" => value is IReadOnlyList<object?>,
            "object" => value is IReadOnlyDictionary<string, object?>,
            "date" => DateTimeOffset.TryParse(value.ToString(), out _),
            _ => true,
        };
    }

    private static bool CheckEnum(object? value, object? allowed)
    {
        if (allowed is not IReadOnlyList<object?> options)
        {
            return true;
        }

        var key = TransformComparer.KeyOf(value);
        return options.Any(o => TransformComparer.KeyOf(o) == key);
    }

    private static int ToInt(object? v) => TransformComparer.TryToDouble(v, out var d) ? (int)d : 0;

    private static double ToDouble(object? v) => TransformComparer.TryToDouble(v, out var d) ? d : 0;
}
