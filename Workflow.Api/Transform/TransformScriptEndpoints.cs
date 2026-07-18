// <copyright file="TransformScriptEndpoints.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Transform;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Workflow.Modules.Transform.Script.Abstractions;
using Workflow.Modules.Builtin.Transform.Internal;
using Workflow.Scripting.Roslyn.Abstractions;
using Workflow.Scripting.Roslyn.Execution;

/// <summary>
/// 🌟 Phase 2.6.b.2 — Minimal-API endpoints for the typed transform-script authoring surface~ ✨💖.
/// </summary>
/// <remarks>
/// Routes: <c>POST /api/transform/script/{validate,preview,compile}</c>. Compile is gated behind a
/// trusted-author header (placeholder until real auth), mirroring the 2.4.b.5 linq endpoints~ 🌸.
/// </remarks>
public static class TransformScriptEndpoints
{
    private const string TrustedAuthorHeader = "X-Trusted-Author";

    /// <summary>Registers the transform-script authoring endpoints~ 🌟.</summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapTransformScriptEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/transform/script").WithTags("Transform Script");

        group.MapPost("/validate", ValidateHandler)
            .WithName("ValidateTransformScript")
            .WithSummary("Compile a transform body without persisting; returns diagnostics");

        group.MapPost("/preview", PreviewHandler)
            .WithName("PreviewTransformScript")
            .WithSummary("Compile + run a transform body against caller-supplied sample rows");

        group.MapPost("/compile", CompileHandler)
            .WithName("CompileTransformScript")
            .WithSummary("Compile + cache a transform body (trusted-author gated); returns the blob key");

        return app;
    }

    private static IResult ValidateHandler(ValidateRequest request, ITransformScriptCompiler compiler)
    {
        var result = compiler.Compile(request.Code ?? string.Empty);
        return Results.Ok(new ValidateResponse(result.Success, ToDtos(result.Diagnostics)));
    }

    private static async Task<IResult> PreviewHandler(PreviewRequest request, ITransformScriptPreviewer previewer, CancellationToken ct)
    {
        // Normalise JSON-deserialised sample data (JsonElement → CLR dict/list/scalar)~ 🧹
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var raw in request.SampleRows ?? new List<Dictionary<string, object?>>())
        {
            if (TransformDataNormalizer.Normalize(raw) is IReadOnlyDictionary<string, object?> rec)
            {
                rows.Add(rec);
            }
        }

        var inputs = TransformDataNormalizer.Normalize(request.Inputs) as IReadOnlyDictionary<string, object?>
            ?? new Dictionary<string, object?>();

        var result = await previewer.PreviewAsync(request.Code ?? string.Empty, rows, inputs, ct: ct).ConfigureAwait(false);
        return Results.Ok(new PreviewResponse(result.Success, result.Result, result.DurationMs, ToDtos(result.Diagnostics)));
    }

    private static async Task<IResult> CompileHandler(
        CompileRequest request,
        HttpContext http,
        ITransformScriptCompiler compiler,
        ICompiledScriptCache cache,
        CancellationToken ct)
    {
        // 🔐 Trusted-author gate (D14) — placeholder until real auth infra lands.
        if (!string.Equals(http.Request.Headers[TrustedAuthorHeader], "true", StringComparison.OrdinalIgnoreCase))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var result = compiler.Compile(request.Code ?? string.Empty);
        if (!result.Success || result.AssemblyBytes is null)
        {
            return Results.BadRequest(new ValidateResponse(false, ToDtos(result.Diagnostics)));
        }

        var inputsFingerprint = string.Join(",", (request.Inputs ?? new Dictionary<string, object?>()).Keys.OrderBy(k => k));
        var key = ScriptAssemblyKey.Build(
            "compiled-modules/transform",
            request.DefinitionId ?? "adhoc",
            request.NodeId ?? "adhoc",
            request.Code ?? string.Empty,
            compiler.SchemaVersion,
            inputsFingerprint);

        await cache.StoreAsync(key, result.AssemblyBytes, ct).ConfigureAwait(false);
        return Results.Ok(new CompileResponse(key));
    }

    private static IReadOnlyList<DiagnosticDto> ToDtos(IReadOnlyList<ScriptDiagnostic> diagnostics)
        => diagnostics.Select(d => new DiagnosticDto(d.Id, d.Severity.ToString(), d.Message, d.Line, d.Column)).ToList();

    /// <summary>Validate/compile request body~ 📥.</summary>
    public record ValidateRequest(string? Code);

    /// <summary>Preview request body~ 📥.</summary>
    public record PreviewRequest(string? Code, List<Dictionary<string, object?>>? SampleRows, Dictionary<string, object?>? Inputs);

    /// <summary>Compile request body~ 📥.</summary>
    public record CompileRequest(string? Code, string? DefinitionId, string? NodeId, Dictionary<string, object?>? Inputs);

    /// <summary>A diagnostic DTO~ 🩺.</summary>
    public record DiagnosticDto(string Id, string Severity, string Message, int? Line, int? Column);

    /// <summary>Validate response~ 📤.</summary>
    public record ValidateResponse(bool Success, IReadOnlyList<DiagnosticDto> Diagnostics);

    /// <summary>Preview response~ 📤.</summary>
    public record PreviewResponse(bool Success, object? Result, long DurationMs, IReadOnlyList<DiagnosticDto> Diagnostics);

    /// <summary>Compile response~ 📤.</summary>
    public record CompileResponse(string CompiledAssemblyKey);
}
