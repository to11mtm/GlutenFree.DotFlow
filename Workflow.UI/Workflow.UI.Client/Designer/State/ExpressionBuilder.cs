// <copyright file="ExpressionBuilder.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// ƒx Round-2 G8 — composes the <c>{{…}}</c> expressions the property binder evaluates at run
/// time, from a binding token (variable or upstream output) plus an optional comparison.
/// Framework-free~ ✨.
/// </summary>
public static class ExpressionBuilder
{
    /// <summary>The "no comparison" operator choice~ 🎛️.</summary>
    public const string NoOperator = "(none)";

    /// <summary>The comparison operators offered by the builder~ 🎛️.</summary>
    public static readonly IReadOnlyList<string> Operators =
        new[] { NoOperator, "==", "!=", ">", ">=", "<", "<=" };

    /// <summary>Common syntax examples shown as hints in the builder~ 💡.</summary>
    public static readonly IReadOnlyList<(string Example, string Meaning)> Hints = new[]
    {
        ("{{Variable.count}}", "the value of workflow variable 'count'"),
        ("{{nodeId.port}}", "an upstream node's output (e.g. {{http-1.body}})"),
        ("{{Variable.count > 5}}", "a comparison — evaluates to true/false"),
        ("{{Variable.status == \"done\"}}", "string comparisons use double quotes"),
        ("Order {{Variable.id}} shipped", "tokens can be embedded inside plain text"),
    };

    /// <summary>Extracts the inner reference of a token (<c>{{Variable.x}}</c> → <c>Variable.x</c>)~ 🔍.</summary>
    /// <param name="token">The token or bare reference.</param>
    /// <returns>The inner reference text.</returns>
    public static string InnerOf(string token)
    {
        var t = token.Trim();
        return t.StartsWith("{{", StringComparison.Ordinal) && t.EndsWith("}}", StringComparison.Ordinal)
            ? t[2..^2].Trim()
            : t;
    }

    /// <summary>
    /// Composes an expression from a source token, an optional operator, and a comparison value.
    /// Without an operator (or value) the plain binding token is returned; with one, the value is
    /// auto-quoted unless it is a number, boolean, or already quoted~ ƒx.
    /// </summary>
    /// <param name="token">The source token (e.g. <c>{{Variable.count}}</c>).</param>
    /// <param name="op">The operator, or <see cref="NoOperator"/>/null.</param>
    /// <param name="value">The right-hand value text.</param>
    /// <returns>The composed <c>{{…}}</c> expression.</returns>
    public static string Compose(string token, string? op, string? value)
    {
        var inner = InnerOf(token);
        if (string.IsNullOrWhiteSpace(op) || op == NoOperator || string.IsNullOrWhiteSpace(value))
        {
            return "{{" + inner + "}}";
        }

        return "{{" + inner + " " + op.Trim() + " " + QuoteIfNeeded(value.Trim()) + "}}";
    }

    private static string QuoteIfNeeded(string value)
    {
        var isNumber = double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _);
        var isBool = value is "true" or "false";
        var isQuoted = value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"');
        return isNumber || isBool || isQuoted ? value : "\"" + value + "\"";
    }
}
