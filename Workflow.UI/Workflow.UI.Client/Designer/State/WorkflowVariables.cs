// <copyright file="WorkflowVariables.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// 💾 Phase 3.5 (V1.1) — reads and writes the workflow's variable declarations, which travel
/// through <see cref="DesignerDocument.Variables"/> as raw JSON (name → serialized
/// <c>VariableDefinition</c>). Framework-free (D2)~ ✨.
/// </summary>
/// <remarks>
/// Two things about the wire format are load-bearing here:
/// <list type="bullet">
/// <item><description>
/// <c>type</c> and <c>seed</c> are enums and **no <c>JsonStringEnumConverter</c> is registered
/// anywhere in the solution**, so they arrive as <em>numbers</em>. Parsing stays tolerant of
/// strings anyway, so nothing breaks if a converter is added later.
/// </description></item>
/// <item><description>
/// Unknown properties are **preserved** on write. The designer must never be the reason a field it
/// doesn't understand disappears from a definition.
/// </description></item>
/// </list>
/// </remarks>
public static class WorkflowVariables
{
    /// <summary>The runtime name rule, matching <c>SetVariableModule</c>'s validation~ 🏷️.</summary>
    public const string NamePattern = "^[a-zA-Z_][a-zA-Z0-9_.]*$";

    private static readonly Regex NameRegex = new(NamePattern, RegexOptions.Compiled);

    /// <summary>The types offered when declaring a variable~ 🎨.</summary>
    /// <remarks>
    /// <c>Connection</c> and <c>Variable</c> are deliberately omitted: they are reference markers in
    /// the module property system, not values a workflow variable would hold. They still parse
    /// correctly if a definition already uses them.
    /// </remarks>
    public static IReadOnlyList<VariableValueType> SelectableTypes { get; } = new[]
    {
        VariableValueType.String,
        VariableValueType.Int,
        VariableValueType.Long,
        VariableValueType.Decimal,
        VariableValueType.Boolean,
        VariableValueType.DateTime,
        VariableValueType.TimeSpan,
        VariableValueType.Guid,
        VariableValueType.Object,
        VariableValueType.Array,
    };

    /// <summary>Lists the document's declared variables, name-ordered~ 📚.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The parsed declarations.</returns>
    public static IReadOnlyList<WorkflowVariable> List(DesignerDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Variables
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => Parse(kv.Key, kv.Value))
            .ToList();
    }

    /// <summary>Parses one declaration, tolerating missing or oddly-typed fields~ 📥.</summary>
    /// <param name="name">The map key (authoritative for the name).</param>
    /// <param name="raw">The raw declaration JSON.</param>
    /// <returns>The parsed declaration.</returns>
    public static WorkflowVariable Parse(string name, JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.Object)
        {
            return new WorkflowVariable(name, VariableValueType.String, null, null, VariableSeed.SeedOnly, false);
        }

        return new WorkflowVariable(
            name,
            ReadEnum(raw, "type", VariableValueType.String),
            TryProperty(raw, "initialValue") is { } iv && iv.ValueKind != JsonValueKind.Undefined ? iv.Clone() : null,
            TryProperty(raw, "description") is { ValueKind: JsonValueKind.String } d ? d.GetString() : null,
            ReadEnum(raw, "seed", VariableSeed.SeedOnly),
            TryProperty(raw, "isSecret") is { } s && JsonValues.AsBool(s));
    }

    /// <summary>
    /// Serializes a declaration back to the wire shape, **preserving any properties the designer
    /// doesn't model** from <paramref name="existing"/>~ 📤.
    /// </summary>
    /// <param name="variable">The declaration to write.</param>
    /// <param name="existing">The previous raw JSON for this variable, when editing.</param>
    /// <returns>The declaration JSON.</returns>
    public static JsonElement ToJson(WorkflowVariable variable, JsonElement? existing = null)
    {
        ArgumentNullException.ThrowIfNull(variable);

        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        // Carry forward anything we don't model, so round-tripping is lossless.
        if (existing is { ValueKind: JsonValueKind.Object } prior)
        {
            foreach (var property in prior.EnumerateObject())
            {
                fields[property.Name] = property.Value.Clone();
            }
        }

        RemoveKnown(fields);

        fields["name"] = JsonValues.FromString(variable.Name);

        // E3 — enum *names*, not ordinals. Workflow definitions are exported to files and reviewed
        // in pull requests, where `"type": 1` tells a reader nothing. Parsing accepts both forms
        // (see ReadEnum), so definitions written before this change still load.
        fields["type"] = JsonValues.FromString(variable.Type.ToString());
        fields["seed"] = JsonValues.FromString(variable.Seed.ToString());
        fields["isSecret"] = JsonValues.FromBool(variable.IsSecret);

        if (!string.IsNullOrWhiteSpace(variable.Description))
        {
            fields["description"] = JsonValues.FromString(variable.Description);
        }

        // A null InitialValue means "not declared"; a JSON null element means "declared as null".
        if (variable.InitialValue is { } initial && initial.ValueKind != JsonValueKind.Undefined)
        {
            fields["initialValue"] = initial;
        }

        return JsonSerializer.SerializeToElement(fields);
    }

    /// <summary>Validates a name for the add/rename flows~ ✅.</summary>
    /// <param name="name">The proposed name.</param>
    /// <param name="existingNames">The names already declared.</param>
    /// <param name="currentName">The name being renamed, so it doesn't collide with itself.</param>
    /// <returns>An error message, or null when the name is acceptable.</returns>
    public static string? ValidateName(
        string? name,
        IEnumerable<string> existingNames,
        string? currentName = null)
    {
        ArgumentNullException.ThrowIfNull(existingNames);

        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return "Give the variable a name.";
        }

        if (!NameRegex.IsMatch(trimmed))
        {
            return "Use a letter or underscore first, then letters, numbers, underscores or dots.";
        }

        // The binder resolves names case-insensitively (PropertyBinder), so 'count' and 'Count'
        // would be the same variable at run time — reject the collision here rather than let it
        // become a confusing runtime aliasing bug (Q6).
        var clash = existingNames.FirstOrDefault(existing =>
            !string.Equals(existing, currentName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase));

        return clash is null
            ? null
            : string.Equals(clash, trimmed, StringComparison.Ordinal)
                ? $"'{trimmed}' is already declared."
                : $"'{clash}' is already declared — names are case-insensitive at run time.";
    }

    /// <summary>Returns whether a name is syntactically valid~ 🔍.</summary>
    /// <param name="name">The name.</param>
    /// <returns>True when it matches <see cref="NamePattern"/>.</returns>
    public static bool IsValidName(string? name)
        => !string.IsNullOrWhiteSpace(name) && NameRegex.IsMatch(name.Trim());

    /// <summary>Maps a declared type to the editor the panel should render~ 🎛️.</summary>
    /// <param name="type">The declared type.</param>
    /// <returns>One of <c>text</c>, <c>number</c>, <c>boolean</c> or <c>json</c>.</returns>
    public static string EditorKindFor(VariableValueType type) => type switch
    {
        VariableValueType.Int or VariableValueType.Long or VariableValueType.Decimal => "number",
        VariableValueType.Boolean => "boolean",
        VariableValueType.Object or VariableValueType.Array => "json",
        _ => "text",
    };

    /// <summary>Builds an initial-value element from editor text for the declared type~ 🔤.</summary>
    /// <param name="text">The editor text (null/empty means "no initial value").</param>
    /// <param name="type">The declared type.</param>
    /// <returns>The element, or null when no initial value is declared. Invalid JSON returns null.</returns>
    public static JsonElement? InitialValueFrom(string? text, VariableValueType type)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return EditorKindFor(type) switch
        {
            "number" => JsonValues.FromNumber(text),
            "boolean" => JsonValues.FromBool(bool.TryParse(text, out var b) && b),
            "json" => JsonValues.TryParseJson(text),
            _ => JsonValues.FromString(text),
        };
    }

    /// <summary>Renders an initial value for editing~ 📝.</summary>
    /// <param name="variable">The declaration.</param>
    /// <returns>The editor text (empty when no initial value is declared).</returns>
    public static string InitialValueText(WorkflowVariable variable)
    {
        ArgumentNullException.ThrowIfNull(variable);
        if (variable.InitialValue is not { } element)
        {
            return string.Empty;
        }

        return EditorKindFor(variable.Type) == "json" ? element.GetRawText() : JsonValues.ToText(element);
    }

    /// <summary>
    /// Lists the ids of nodes whose property values reference <paramref name="name"/>~ 🔗.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="name">The variable name.</param>
    /// <returns>The distinct referencing node ids, in document order.</returns>
    /// <remarks>
    /// Matching mirrors the binder: names resolve case-insensitively, and a trailing dot is a
    /// property path into the value — so <c>{{Variable.user.name}}</c> counts as a reference to
    /// <c>user</c>, while <c>{{Variable.username}}</c> does not.
    /// </remarks>
    public static IReadOnlyList<string> ReferencingNodes(DesignerDocument document, string name)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!IsValidName(name))
        {
            return [];
        }

        var pattern = ReferenceRegexFor(name);
        return document.Nodes
            .Where(node => node.Properties.Values.Any(value =>
                value.ValueKind == JsonValueKind.String && pattern.IsMatch(value.GetString() ?? string.Empty)))
            .Select(node => node.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Rewrites references to <paramref name="oldName"/> so they point at <paramref name="newName"/>~ ✏️.</summary>
    /// <param name="value">The property value text.</param>
    /// <param name="oldName">The current variable name.</param>
    /// <param name="newName">The new variable name.</param>
    /// <returns>The rewritten text.</returns>
    public static string RenameReferences(string value, string oldName, string newName)
    {
        if (string.IsNullOrEmpty(value) || !IsValidName(oldName) || !IsValidName(newName))
        {
            return value;
        }

        return ReferenceRegexFor(oldName).Replace(value, "Variable." + newName.Trim());
    }

    private static Regex ReferenceRegexFor(string name)
        => new(
            $@"(?<![A-Za-z0-9_.])Variable\.{Regex.Escape(name.Trim())}(?![A-Za-z0-9_])",
            RegexOptions.IgnoreCase);

    private static void RemoveKnown(Dictionary<string, JsonElement> fields)
    {
        // Drop any casing variant of the fields we're about to write, so a definition that arrived
        // with PascalCase keys doesn't end up with both spellings.
        foreach (var key in fields.Keys
                     .Where(k => KnownFields.Contains(k, StringComparer.OrdinalIgnoreCase))
                     .ToList())
        {
            fields.Remove(key);
        }
    }

    private static readonly string[] KnownFields =
        ["name", "type", "initialValue", "description", "seed", "isSecret"];

    private static JsonElement? TryProperty(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var exact))
        {
            return exact;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static TEnum ReadEnum<TEnum>(JsonElement element, string name, TEnum fallback)
        where TEnum : struct, Enum
    {
        if (TryProperty(element, name) is not { } value)
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var i) && Enum.IsDefined(typeof(TEnum), i)
                => (TEnum)Enum.ToObject(typeof(TEnum), i),
            JsonValueKind.String when Enum.TryParse<TEnum>(value.GetString(), ignoreCase: true, out var parsed)
                => parsed,
            _ => fallback,
        };
    }
}
