// <copyright file="ApiResults.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.V1;

using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Workflow.Core.Models;

/// <summary>
/// 🚨 RFC 7807 ProblemDetails helpers shared by the v1 endpoints (Phase 2.7.0 / D8)~ ✨.
/// </summary>
public static class ApiResults
{
    /// <summary>Creates a 404 Not Found problem~ 🔍.</summary>
    /// <param name="detail">Human-readable detail.</param>
    /// <returns>A ProblemDetails result.</returns>
    public static IResult NotFoundProblem(string detail)
        => Results.Problem(detail: detail, statusCode: StatusCodes.Status404NotFound, title: "Not Found");

    /// <summary>Creates a 409 Conflict problem~ ⚔️.</summary>
    /// <param name="detail">Human-readable detail.</param>
    /// <returns>A ProblemDetails result.</returns>
    public static IResult ConflictProblem(string detail)
        => Results.Problem(detail: detail, statusCode: StatusCodes.Status409Conflict, title: "Conflict");

    /// <summary>Creates a 422 validation problem from a <see cref="ValidationResult"/>~ 🛡️.</summary>
    /// <param name="validation">The failed validation result.</param>
    /// <returns>A ValidationProblem result (422).</returns>
    public static IResult ValidationProblem422(ValidationResult validation)
    {
        var errors = new Dictionary<string, string[]>();
        foreach (var error in validation.Errors)
        {
            var key = string.IsNullOrEmpty(error.PropertyName) ? error.Code : error.PropertyName!;
            errors[key] = errors.TryGetValue(key, out var existing)
                ? Append(existing, error.Message)
                : new[] { error.Message };
        }

        return Results.ValidationProblem(errors, statusCode: StatusCodes.Status422UnprocessableEntity, title: "Validation Failed");
    }

    /// <summary>Creates a 400 Bad Request problem~ 🚫.</summary>
    /// <param name="detail">Human-readable detail.</param>
    /// <returns>A ProblemDetails result.</returns>
    public static IResult BadRequestProblem(string detail)
        => Results.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest, title: "Bad Request");

    /// <summary>Creates a 503 Service Unavailable problem~ 🚧.</summary>
    /// <param name="detail">Human-readable detail.</param>
    /// <returns>A ProblemDetails result.</returns>
    public static IResult ServiceUnavailableProblem(string detail)
        => Results.Problem(detail: detail, statusCode: StatusCodes.Status503ServiceUnavailable, title: "Service Unavailable");

    private static string[] Append(string[] existing, string value)
    {
        var result = new string[existing.Length + 1];
        existing.CopyTo(result, 0);
        result[^1] = value;
        return result;
    }
}
