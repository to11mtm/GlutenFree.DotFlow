// <copyright file="SqlParameterTemplateResolver.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Internal;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Workflow.Modules.Database.Abstractions;

/// <summary>
/// 🔗 Resolves <c>{{…}}</c> binding tokens inside SQL parameter <em>values</em> so users can feed
/// parameters from workflow variables (<c>{{Variable.name}}</c>) or upstream node outputs
/// (<c>{{nodeId.port}}</c>). Values still flow through <see cref="SqlParameterBinder"/> afterwards,
/// so they always bind as parameters and are never concatenated into the SQL (D7)~ ✨.
/// </summary>
/// <remarks>
/// Phase 3.5 (V4) made the engine resolve templates in node <em>properties</em>, which removed the
/// original reason this existed. It stays because the property rule only reaches <b>top-level
/// string</b> properties: the parameter map is a <c>Json</c> property, and the tokens live one
/// level down in its <em>values</em>. The SQL text itself is deliberately never expanded on either
/// path. The <c>\{\{</c> escape is honoured here too, so the syntax is identical everywhere.
/// </remarks>
public static class SqlParameterTemplateResolver
{
    private const string VariablePrefix = "Variable.";

    /// <summary>The escape sequence for a literal <c>{{</c>, matching <c>PropertyBinder</c>~ 🚪.</summary>
    private const string EscapedOpenBrace = @"\{\{";

    private static readonly Regex TokenPattern = new(@"(?<!\\)\{\{\s*([^{}]+?)\s*\}\}", RegexOptions.Compiled);

    /// <summary>
    /// Resolves tokens in every string value of a parameter map. A value that is exactly one
    /// token resolves to the referenced value with its runtime type preserved; tokens embedded
    /// in longer text are replaced with their string form. Non-string values pass through~ 🔗.
    /// </summary>
    /// <param name="parameters">The normalised parameter map (may be null).</param>
    /// <param name="inputs">The node's inputs (contains <c>nodeId.port</c>-prefixed upstream outputs).</param>
    /// <param name="variables">The workflow variables.</param>
    /// <returns>The resolved map (same instance when null or nothing to resolve).</returns>
    /// <exception cref="SqlParameterBindingException">Thrown when a token cannot be resolved.</exception>
    public static IReadOnlyDictionary<string, object?>? Resolve(
        IReadOnlyDictionary<string, object?>? parameters,
        IReadOnlyDictionary<string, object?> inputs,
        IReadOnlyDictionary<string, object?> variables)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return parameters;
        }

        Dictionary<string, object?>? resolved = null;
        foreach (var (name, value) in parameters)
        {
            if (value is not string text)
            {
                continue;
            }

            var hasToken = TokenPattern.IsMatch(text);
            var hasEscape = text.Contains(EscapedOpenBrace, StringComparison.Ordinal);
            if (!hasToken && !hasEscape)
            {
                continue;
            }

            resolved ??= new Dictionary<string, object?>(parameters, StringComparer.Ordinal);

            if (!hasToken)
            {
                // Only an escaped literal — unescape and move on.
                resolved[name] = Unescape(text);
                continue;
            }

            var pure = TokenPattern.Match(text);
            if (pure.Success && pure.Index == 0 && pure.Length == text.Length)
            {
                // Whole-value token → typed value (numbers stay numbers, etc.)~ 🎯
                resolved[name] = Lookup(name, pure.Groups[1].Value, inputs, variables);
            }
            else
            {
                // Embedded token(s) → string interpolation~ 🧵
                resolved[name] = Unescape(TokenPattern.Replace(
                    text,
                    m => Lookup(name, m.Groups[1].Value, inputs, variables)?.ToString() ?? string.Empty));
            }
        }

        return resolved ?? parameters;
    }

    private static string Unescape(string text)
        => text.Contains(EscapedOpenBrace, StringComparison.Ordinal)
            ? text.Replace(EscapedOpenBrace, "{{", StringComparison.Ordinal)
            : text;

    private static object? Lookup(
        string paramName,
        string reference,
        IReadOnlyDictionary<string, object?> inputs,
        IReadOnlyDictionary<string, object?> variables)
    {
        if (reference.StartsWith(VariablePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var varName = reference[VariablePrefix.Length..];
            if (variables.TryGetValue(varName, out var varValue))
            {
                return varValue;
            }

            throw new SqlParameterBindingException(
                paramName,
                $"Binding '{{{{{reference}}}}}' could not be resolved: workflow variable '{varName}' was not found~");
        }

        // Upstream output ('nodeId.port') or a plain input port name — both live in Inputs.
        if (inputs.TryGetValue(reference, out var inputValue))
        {
            return inputValue;
        }

        throw new SqlParameterBindingException(
            paramName,
            $"Binding '{{{{{reference}}}}}' could not be resolved: no input '{reference}' was available. " +
            "Use {{Variable.name}} for variables or {{nodeId.port}} for a connected upstream output~");
    }
}
