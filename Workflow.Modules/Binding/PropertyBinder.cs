// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

namespace Workflow.Modules.Binding;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using LanguageExt;
using Workflow.Core.Models;

/// <summary>
/// ⚙️ Default implementation of <see cref="IPropertyBinder"/>.
/// Resolves variable/node-output references, converts types, applies defaults,
/// validates against schema, and accumulates all errors. The full binding pipeline~ 💖.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: The binding pipeline for each port runs in this order:
/// 1. Look up raw value by port name.
/// 2. If missing → apply default or flag required error.
/// 3. If string → check for <c>{{...}}</c> reference patterns and resolve.
/// 4. Convert to expected <see cref="PortDefinition.DataType"/>.
/// 5. Validate the final value type matches schema.
/// 6. Accumulate errors, never short-circuit! UwU ✨.
/// </para>
/// </remarks>
public class PropertyBinder : IPropertyBinder
{
    /// <summary>
    /// Prefix used to identify variable references (case-insensitive). 💾.
    /// </summary>
    private const string VariablePrefix = "Variable.";

    /// <summary>
    /// Regex pattern for detecting template references like <c>{{Variable.Name}}</c>
    /// or <c>{{NodeId.OutputName}}</c>. Captures the inner expression. 🔍.
    /// </summary>
    /// <remarks>
    /// CopilotNote: The inner group captures everything between {{ and }},
    /// trimmed of whitespace during processing. Supports dots for nested access!
    /// E.g., <c>{{Variable.User.Name}}</c> → inner = "Variable.User.Name". 💫.
    /// </remarks>
    private static readonly Regex ReferencePattern = new(
        @"\{\{\s*(.+?)\s*\}\}",
        RegexOptions.Compiled);

    /// <inheritdoc />
    public PropertyBindingResult BindProperties(
        IReadOnlyDictionary<string, object?> rawValues,
        Arr<PortDefinition> schema,
        PropertyBindingContext context)
    {
        ArgumentNullException.ThrowIfNull(rawValues);
        ArgumentNullException.ThrowIfNull(context);

        var boundValues = new Dictionary<string, object?>();
        var errors = new List<string>();

        foreach (var port in schema)
        {
            var portName = port.Name;

            // Step 1: Look up raw value by port name. 🔍
            var hasRawValue = TryGetRawValue(rawValues, portName, out var rawValue);

            // Step 2: Handle missing values — apply default or flag error. 💫
            if (!hasRawValue || rawValue == null)
            {
                if (port.DefaultValue != null)
                {
                    // Apply default value. ✨
                    boundValues[portName] = port.DefaultValue;
                    continue;
                }

                if (port.IsRequired)
                {
                    errors.Add($"Required input '{portName}' is missing and has no default value.");
                    continue;
                }

                // Optional with no default — set to null and move on. 🌙
                boundValues[portName] = null;
                continue;
            }

            // Step 3: Resolve references if the raw value is a string with {{...}} patterns. 🔗
            var resolvedValue = rawValue;
            if (rawValue is string stringValue && ReferencePattern.IsMatch(stringValue))
            {
                var resolution = ResolveReferences(stringValue, context, portName);
                if (resolution.HasErrors)
                {
                    errors.AddRange(resolution.Errors);
                    continue;
                }

                resolvedValue = resolution.ResolvedValue;
            }

            // Step 4: Convert to expected data type. 🔄
            var conversion = ConvertToExpectedType(resolvedValue, port.DataType, portName);
            if (conversion.HasErrors)
            {
                errors.AddRange(conversion.Errors);
                continue;
            }

            // Step 5: Validate the final value type (safety check). ✅
            var finalValue = conversion.ConvertedValue;
            if (finalValue != null && port.DataType != typeof(object) && !port.DataType.IsInstanceOfType(finalValue))
            {
                errors.Add(
                    $"Input '{portName}': converted value type '{finalValue.GetType().Name}' " +
                    $"does not match expected type '{port.DataType.Name}'.");
                continue;
            }

            boundValues[portName] = finalValue;
        }

        // Also pass through any extra values not in the schema (for flexibility). 🎁
        foreach (var (key, value) in rawValues)
        {
            if (!boundValues.ContainsKey(key) && !schema.Any(p => p.Name == key))
            {
                boundValues[key] = value;
            }
        }

        return errors.Count > 0
            ? new PropertyBindingResult(false, boundValues, errors.ToArr())
            : PropertyBindingResult.Ok(boundValues);
    }

    /// <summary>
    /// Attempts to find a raw value by port name (exact match, then case-insensitive fallback).
    /// </summary>
    private static bool TryGetRawValue(
        IReadOnlyDictionary<string, object?> rawValues,
        string portName,
        out object? value)
    {
        // Exact match first. ✨
        if (rawValues.TryGetValue(portName, out value))
        {
            return true;
        }

        // Case-insensitive fallback. 🔍
        var match = rawValues.Keys.FirstOrDefault(k =>
            k.Equals(portName, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            value = rawValues[match];
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Resolves <c>{{...}}</c> reference patterns within a string value.
    /// Supports <c>{{Variable.Name}}</c> and <c>{{NodeId.OutputName}}</c> patterns. 💫.
    /// </summary>
    /// <remarks>
    /// CopilotNote: If the ENTIRE string is a single reference (e.g., <c>"{{Variable.Count}}"</c>),
    /// the resolved value keeps its original type (int stays int). If the string CONTAINS
    /// references mixed with text (e.g., <c>"Hello {{Variable.Name}}!"</c>), the result
    /// is always a string with interpolated values. Smart and kawaii. 💖.
    /// </remarks>
    private static ReferenceResolution ResolveReferences(
        string stringValue,
        PropertyBindingContext context,
        string portName)
    {
        var matches = ReferencePattern.Matches(stringValue);
        if (matches.Count == 0)
        {
            return ReferenceResolution.Resolved(stringValue);
        }

        // If the entire string is a single reference, preserve the resolved type. 🎯
        if (matches.Count == 1 && matches[0].Value == stringValue.Trim())
        {
            var innerExpr = matches[0].Groups[1].Value.Trim();
            var resolution = ResolveSingleReference(innerExpr, context, portName);
            return resolution;
        }

        // Multiple references or mixed text — interpolate as string. 📝
        var errors = new List<string>();
        var result = ReferencePattern.Replace(stringValue, match =>
        {
            var innerExpr = match.Groups[1].Value.Trim();
            var resolution = ResolveSingleReference(innerExpr, context, portName);
            if (resolution.HasErrors)
            {
                errors.AddRange(resolution.Errors);
                return match.Value; // Leave unresolved in output
            }

            return resolution.ResolvedValue?.ToString() ?? string.Empty;
        });

        return errors.Count > 0
            ? ReferenceResolution.Failed(errors)
            : ReferenceResolution.Resolved(result);
    }

    /// <summary>
    /// Resolves a single reference expression (the part inside <c>{{ }}</c>).
    /// Routes to variable resolution or node output resolution. 🔀.
    /// </summary>
    private static ReferenceResolution ResolveSingleReference(
        string expression,
        PropertyBindingContext context,
        string portName)
    {
        // Check for Variable.X pattern. 💾
        if (expression.StartsWith(VariablePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var variablePath = expression.Substring(VariablePrefix.Length);
            return ResolveVariableReference(variablePath, context, portName);
        }

        // Otherwise, treat as NodeId.OutputName pattern. 📤
        var dotIndex = expression.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex > 0 && dotIndex < expression.Length - 1)
        {
            var nodeId = expression.Substring(0, dotIndex);
            var outputPath = expression.Substring(dotIndex + 1);
            return ResolveNodeOutputReference(nodeId, outputPath, context, portName);
        }

        // No dot at all — might be a plain variable name (shorthand). 🤷
        return ReferenceResolution.Failed(new List<string>
        {
            $"Input '{portName}': unrecognized reference pattern '{{{{{expression}}}}}'. " +
            $"Expected '{{{{Variable.Name}}}}' or '{{{{NodeId.OutputName}}}}'.",
        });
    }

    /// <summary>
    /// Resolves a <c>{{Variable.Name}}</c> or <c>{{Variable.User.Name}}</c> reference
    /// by traversing the variable dictionary with dot-notation. 💾.
    /// </summary>
    private static ReferenceResolution ResolveVariableReference(
        string variablePath,
        PropertyBindingContext context,
        string portName)
    {
        var segments = variablePath.Split('.');
        var rootName = segments[0];

        // Look up root variable. 🔍
        if (!context.Variables.TryGetValue(rootName, out var current))
        {
            // Case-insensitive fallback. ✨
            var match = context.Variables.Keys.FirstOrDefault(k =>
                k.Equals(rootName, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                return ReferenceResolution.Failed(new List<string>
                {
                    $"Input '{portName}': variable '{rootName}' not found in binding context.",
                });
            }

            current = context.Variables[match];
        }

        // Traverse nested segments (dot-notation). 🔗
        for (int i = 1; i < segments.Length; i++)
        {
            if (current == null)
            {
                return ReferenceResolution.Failed(new List<string>
                {
                    $"Input '{portName}': cannot traverse into null at '{string.Join(".", segments.Take(i))}' " +
                    $"while resolving '{{{{Variable.{variablePath}}}}}'.",
                });
            }

            current = TraverseProperty(current, segments[i]);
            if (current == null && i < segments.Length - 1)
            {
                return ReferenceResolution.Failed(new List<string>
                {
                    $"Input '{portName}': property '{segments[i]}' not found on object at " +
                    $"'{string.Join(".", segments.Take(i))}' while resolving '{{{{Variable.{variablePath}}}}}'.",
                });
            }
        }

        return ReferenceResolution.Resolved(current);
    }

    /// <summary>
    /// Resolves a <c>{{NodeId.OutputName}}</c> reference from predecessor node outputs. 📤.
    /// </summary>
    private static ReferenceResolution ResolveNodeOutputReference(
        string nodeId,
        string outputPath,
        PropertyBindingContext context,
        string portName)
    {
        // Look up the node's outputs. 🔍
        if (!context.NodeOutputs.TryGetValue(nodeId, out var nodeOutputs))
        {
            // Case-insensitive fallback. ✨
            var match = context.NodeOutputs.Keys.FirstOrDefault(k =>
                k.Equals(nodeId, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                return ReferenceResolution.Failed(new List<string>
                {
                    $"Input '{portName}': node '{nodeId}' not found in binding context node outputs.",
                });
            }

            nodeOutputs = context.NodeOutputs[match];
        }

        // Get the output value. 📦
        var outputSegments = outputPath.Split('.');
        var outputName = outputSegments[0];

        if (!nodeOutputs.TryGetValue(outputName, out var outputValue))
        {
            // Case-insensitive fallback. ✨
            var outputMatch = nodeOutputs.Keys.FirstOrDefault(k =>
                k.Equals(outputName, StringComparison.OrdinalIgnoreCase));
            if (outputMatch == null)
            {
                return ReferenceResolution.Failed(new List<string>
                {
                    $"Input '{portName}': output '{outputName}' not found on node '{nodeId}'.",
                });
            }

            outputValue = nodeOutputs[outputMatch];
        }

        // Traverse nested segments if any. 🔗
        var current = outputValue;
        for (int i = 1; i < outputSegments.Length; i++)
        {
            if (current == null)
            {
                return ReferenceResolution.Failed(new List<string>
                {
                    $"Input '{portName}': cannot traverse into null at " +
                    $"'{nodeId}.{string.Join(".", outputSegments.Take(i))}' while resolving reference.",
                });
            }

            current = TraverseProperty(current, outputSegments[i]);
        }

        return ReferenceResolution.Resolved(current);
    }

    /// <summary>
    /// Traverses a property on an object by name, supporting dictionaries and
    /// <see cref="JsonElement"/> objects for nested access. 🔗.
    /// </summary>
    private static object? TraverseProperty(object obj, string propertyName)
    {
        // Dictionary access (most common for workflow data). 📖
        if (obj is IReadOnlyDictionary<string, object?> readOnlyDict)
        {
            if (readOnlyDict.TryGetValue(propertyName, out var val))
            {
                return val;
            }

            // Case-insensitive fallback.
            var match = readOnlyDict.Keys.FirstOrDefault(k =>
                k.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
            return match != null ? readOnlyDict[match] : null;
        }

        if (obj is IDictionary<string, object?> dict)
        {
            if (dict.TryGetValue(propertyName, out var val))
            {
                return val;
            }

            var match = dict.Keys.FirstOrDefault(k =>
                k.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
            return match != null ? dict[match] : null;
        }

        // JsonElement traversal. 📋
        if (obj is JsonElement jsonEl && jsonEl.ValueKind == JsonValueKind.Object)
        {
            if (jsonEl.TryGetProperty(propertyName, out var prop))
            {
                return ConvertJsonElement(prop);
            }

            return null;
        }

        // Reflection fallback for POCO objects. 🪞
        var bindingFlags =
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.IgnoreCase;
        var propInfo = obj.GetType().GetProperty(propertyName, bindingFlags);
        return propInfo?.GetValue(obj);
    }

    /// <summary>
    /// Converts a resolved value to the expected data type specified by the port schema.
    /// Handles string-to-primitive parsing, numeric widening, JSON deserialization,
    /// and pass-through for compatible types. 🔄✨.
    /// </summary>
    private static ConversionResult ConvertToExpectedType(
        object? value,
        Type expectedType,
        string portName)
    {
        // Null is always valid for nullable/reference types. 🌙
        if (value == null)
        {
            return ConversionResult.Converted(null);
        }

        var actualType = value.GetType();

        // Already the right type (or assignable) — pass through! ✨
        if (expectedType == typeof(object) || expectedType.IsAssignableFrom(actualType))
        {
            return ConversionResult.Converted(value);
        }

        // String → target type conversions. 📝
        if (value is string str)
        {
            return ConvertFromString(str, expectedType, portName);
        }

        // JsonElement → target type. 📋
        if (value is JsonElement jsonElement)
        {
            return ConvertFromJsonElement(jsonElement, expectedType, portName);
        }

        // Numeric widening conversions (int → long, float → double, etc.). 🔢
        if (IsNumericType(actualType) && IsNumericType(expectedType))
        {
            try
            {
                var converted = Convert.ChangeType(value, expectedType, CultureInfo.InvariantCulture);
                return ConversionResult.Converted(converted);
            }
            catch (Exception ex)
            {
                return ConversionResult.Failed(
                    $"Input '{portName}': numeric conversion from '{actualType.Name}' to " +
                    $"'{expectedType.Name}' failed: {ex.Message}");
            }
        }

        // Last resort: try IConvertible. 🎲
        if (value is IConvertible)
        {
            try
            {
                var converted = Convert.ChangeType(value, expectedType, CultureInfo.InvariantCulture);
                return ConversionResult.Converted(converted);
            }
            catch (Exception)
            {
                // Fall through to error
            }
        }

        return ConversionResult.Failed(
            $"Input '{portName}': cannot convert value of type '{actualType.Name}' " +
            $"to expected type '{expectedType.Name}'.");
    }

    /// <summary>
    /// Converts a string to the expected type via parsing. Supports all common
    /// primitives, dates, GUIDs, TimeSpans, and JSON for complex objects. 📝✨.
    /// </summary>
    private static ConversionResult ConvertFromString(
        string str,
        Type expectedType,
        string portName)
    {
        try
        {
            // String → String (identity). 📝
            if (expectedType == typeof(string))
            {
                return ConversionResult.Converted(str);
            }

            // String → Int. 🔢
            if (expectedType == typeof(int))
            {
                return int.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var i)
                    ? ConversionResult.Converted(i)
                    : ConversionResult.Failed($"Input '{portName}': cannot parse '{str}' as int.");
            }

            // String → Long. 📊
            if (expectedType == typeof(long))
            {
                return long.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var l)
                    ? ConversionResult.Converted(l)
                    : ConversionResult.Failed($"Input '{portName}': cannot parse '{str}' as long.");
            }

            // String → Double. 📐
            if (expectedType == typeof(double))
            {
                return double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
                    ? ConversionResult.Converted(d)
                    : ConversionResult.Failed($"Input '{portName}': cannot parse '{str}' as double.");
            }

            // String → Float. 🎈
            if (expectedType == typeof(float))
            {
                return float.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var f)
                    ? ConversionResult.Converted(f)
                    : ConversionResult.Failed($"Input '{portName}': cannot parse '{str}' as float.");
            }

            // String → Decimal. 💰
            if (expectedType == typeof(decimal))
            {
                return decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var m)
                    ? ConversionResult.Converted(m)
                    : ConversionResult.Failed($"Input '{portName}': cannot parse '{str}' as decimal.");
            }

            // String → Bool. ✅
            if (expectedType == typeof(bool))
            {
                return bool.TryParse(str, out var b)
                    ? ConversionResult.Converted(b)
                    : ConversionResult.Failed($"Input '{portName}': cannot parse '{str}' as bool.");
            }

            // String → DateTime. 📅
            if (expectedType == typeof(DateTime))
            {
                return DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
                    ? ConversionResult.Converted(dt)
                    : ConversionResult.Failed($"Input '{portName}': cannot parse '{str}' as DateTime.");
            }

            // String → DateTimeOffset. 📅
            if (expectedType == typeof(DateTimeOffset))
            {
                return DateTimeOffset.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
                    ? ConversionResult.Converted(dto)
                    : ConversionResult.Failed($"Input '{portName}': cannot parse '{str}' as DateTimeOffset.");
            }

            // String → Guid. 🎲
            if (expectedType == typeof(Guid))
            {
                return Guid.TryParse(str, out var g)
                    ? ConversionResult.Converted(g)
                    : ConversionResult.Failed($"Input '{portName}': cannot parse '{str}' as Guid.");
            }

            // String → TimeSpan. ⏱️
            if (expectedType == typeof(TimeSpan))
            {
                return TimeSpan.TryParse(str, CultureInfo.InvariantCulture, out var ts)
                    ? ConversionResult.Converted(ts)
                    : ConversionResult.Failed($"Input '{portName}': cannot parse '{str}' as TimeSpan.");
            }

            // String → Complex object via JSON deserialization. 📋
            try
            {
                var deserialized = JsonSerializer.Deserialize(str, expectedType);
                if (deserialized != null)
                {
                    return ConversionResult.Converted(deserialized);
                }
            }
            catch (JsonException)
            {
                // Fall through to generic error
            }

            return ConversionResult.Failed(
                $"Input '{portName}': cannot convert string value to '{expectedType.Name}'.");
        }
        catch (Exception ex)
        {
            return ConversionResult.Failed(
                $"Input '{portName}': conversion error: {ex.Message}");
        }
    }

    /// <summary>
    /// Converts a <see cref="JsonElement"/> to the expected type. 📋.
    /// </summary>
    private static ConversionResult ConvertFromJsonElement(
        JsonElement element,
        Type expectedType,
        string portName)
    {
        try
        {
            // Try direct deserialization first. ✨
            var deserialized = JsonSerializer.Deserialize(element.GetRawText(), expectedType);
            return ConversionResult.Converted(deserialized);
        }
        catch (Exception ex)
        {
            return ConversionResult.Failed(
                $"Input '{portName}': cannot deserialize JSON to '{expectedType.Name}': {ex.Message}");
        }
    }

    /// <summary>
    /// Converts a <see cref="JsonElement"/> to a regular .NET object (for untyped scenarios). 📋.
    /// </summary>
    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
            _ => element.ToString(),
        };
    }

    /// <summary>
    /// Checks if a type is a numeric type. 🔢.
    /// </summary>
    private static bool IsNumericType(Type type)
    {
        return type == typeof(byte) || type == typeof(sbyte) ||
               type == typeof(short) || type == typeof(ushort) ||
               type == typeof(int) || type == typeof(uint) ||
               type == typeof(long) || type == typeof(ulong) ||
               type == typeof(float) || type == typeof(double) ||
               type == typeof(decimal);
    }

    /// <summary>
    /// Internal result for reference resolution operations. 🔗.
    /// </summary>
    private readonly struct ReferenceResolution
    {
        private ReferenceResolution(object? resolvedValue, IReadOnlyList<string> errors)
        {
            ResolvedValue = resolvedValue;
            Errors = errors;
        }

        public object? ResolvedValue { get; }

        public IReadOnlyList<string> Errors { get; }

        public bool HasErrors => Errors.Count > 0;

        public static ReferenceResolution Resolved(object? value) =>
            new(value, Array.Empty<string>());

        public static ReferenceResolution Failed(IReadOnlyList<string> errors) =>
            new(null, errors);
    }

    /// <summary>
    /// Internal result for type conversion operations. 🔄.
    /// </summary>
    private readonly struct ConversionResult
    {
        private ConversionResult(object? convertedValue, IReadOnlyList<string> errors)
        {
            ConvertedValue = convertedValue;
            Errors = errors;
        }

        public object? ConvertedValue { get; }

        public IReadOnlyList<string> Errors { get; }

        public bool HasErrors => Errors.Count > 0;

        public static ConversionResult Converted(object? value) =>
            new(value, Array.Empty<string>());

        public static ConversionResult Failed(string error) =>
            new(null, new[] { error });
    }
}
