// <copyright file="LoopIterationCap.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Flow;

using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Workflow.Core.Models;

/// <summary>
/// The <c>maxIterations</c> check shared by ForEach and While. A cap below 1 can't run a single
/// iteration: the loop executor fails before the first one ("exceeded maxIterations limit of 0"),
/// even for an empty collection. Reported on save as a warning and blocked at run (§9.4).
/// </summary>
internal static class LoopIterationCap
{
    /// <summary>The validation error code.</summary>
    public const string InvalidCode = "MAX_ITERATIONS_INVALID";

    /// <summary>Validates <c>maxIterations</c> when it's a number (absent = the default 1000; a template is checked at run).</summary>
    /// <param name="configuration">The node's configuration.</param>
    /// <returns>Success, or the error.</returns>
    public static ValidationResult Validate(IReadOnlyDictionary<string, object?> configuration)
    {
        if (!configuration.TryGetValue("maxIterations", out var raw) || AsNumber(raw) is not { } cap || cap >= 1)
        {
            return ValidationResult.Success();
        }

        return ValidationResult.Failure(new ValidationError(
            InvalidCode,
            $"maxIterations must be at least 1 (got {cap.ToString(CultureInfo.InvariantCulture)}); leave it empty for the default of 1000",
            PropertyName: "maxIterations"));
    }

    private static double? AsNumber(object? value) => value switch
    {
        int i => i,
        long l => l,
        double d => d,
        decimal m => (double)m,
        JsonElement { ValueKind: JsonValueKind.Number } json => json.GetDouble(),
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null,
    };
}
