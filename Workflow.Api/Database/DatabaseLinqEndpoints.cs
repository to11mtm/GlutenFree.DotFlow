// <copyright file="DatabaseLinqEndpoints.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Database;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Workflow.Core.Models;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Catalog;
using Workflow.Modules.Database.Linq.Abstractions;
using Workflow.Modules.Database.Linq.Execution;

/// <summary>
/// 🧬 Phase 2.4.b.5 — Minimal-API endpoints for the typed linq authoring surface~ ✨💖.
/// </summary>
/// <remarks>
/// Routes:
/// <list type="bullet">
///   <item><description><c>POST /api/database/linq/validate</c> — compile without persisting</description></item>
///   <item><description><c>POST /api/database/linq/preview</c> — compile + run in the sandbox</description></item>
///   <item><description><c>POST /api/database/linq/compile</c> — compile + cache (trusted-author gated)</description></item>
///   <item><description><c>POST /api/database/catalog/{connectionId}/import</c> — schema import (404 unknown)</description></item>
/// </list>
/// </remarks>
public static class DatabaseLinqEndpoints
{
    /// <summary>Registers the typed-linq authoring endpoints~ 🧬.</summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapDatabaseLinqEndpoints(this IEndpointRouteBuilder app)
    {
        var linq = app.MapGroup("/api/database/linq").WithTags("Database Linq");

        linq.MapPost("/validate", ValidateHandler)
            .WithName("ValidateLinq")
            .WithSummary("Compile a typed linq body without persisting; returns diagnostics")
            .Produces<ValidateResponse>(StatusCodes.Status200OK);

        linq.MapPost("/preview", PreviewHandler)
            .WithName("PreviewLinq")
            .WithSummary("Compile + run a typed linq body against a seeded rollback-only sandbox")
            .Produces<PreviewResponse>(StatusCodes.Status200OK);

        linq.MapPost("/compile", CompileHandler)
            .WithName("CompileLinq")
            .WithSummary("Compile + cache a typed linq body (trusted-author gated); returns the blob key")
            .Produces<CompileResponse>(StatusCodes.Status200OK)
            .Produces<ValidateResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden);

        app.MapPost("/api/database/catalog/{connectionId}/import", CatalogImportHandler)
            .WithTags("Database Linq")
            .WithName("ImportCatalog")
            .WithSummary("Introspect a connection's schema and populate the workflow table catalog")
            .Produces<CatalogImportResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        // 📚 Linq Studio L0 — catalog management (list / manual upsert / remove)~
        app.MapGet("/api/database/catalog/{connectionId}", CatalogListHandler)
            .WithTags("Database Linq")
            .WithName("ListCatalog")
            .WithSummary("List the catalogued tables (with columns) for a named connection")
            .Produces<CatalogTableDto[]>(StatusCodes.Status200OK);

        app.MapPut("/api/database/catalog/{connectionId}/{tableName}", CatalogUpsertHandler)
            .WithTags("Database Linq")
            .WithName("UpsertCatalogTable")
            .WithSummary("Manually define or update a catalogued table for a named connection")
            .Produces<CatalogTableDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        app.MapDelete("/api/database/catalog/{connectionId}/{tableName}", CatalogRemoveHandler)
            .WithTags("Database Linq")
            .WithName("RemoveCatalogTable")
            .WithSummary("Remove a catalogued table")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ValidateHandler(
        LinqAuthoringRequest request,
        IWorkflowLinqCompiler compiler,
        IWorkflowTableCatalog catalog,
        CancellationToken ct)
    {
        var compileRequest = await BuildCompileRequest(request, catalog, ct).ConfigureAwait(false);
        var result = await compiler.CompileAsync(compileRequest, ct).ConfigureAwait(false);
        return Results.Ok(new ValidateResponse(result.Success, ToDtos(result.Errors), ToDtos(result.Warnings)));
    }

    private static async Task<IResult> PreviewHandler(
        LinqAuthoringRequest request,
        IWorkflowLinqPreviewer previewer,
        IWorkflowTableCatalog catalog,
        CancellationToken ct)
    {
        var compileRequest = await BuildCompileRequest(request, catalog, ct).ConfigureAwait(false);
        var previewRequest = new LinqPreviewRequest(compileRequest, CoerceInputs(request));
        var result = await previewer.PreviewAsync(previewRequest, ct).ConfigureAwait(false);

        return Results.Ok(new PreviewResponse(
            result.Success,
            result.Rows,
            result.RowCount,
            result.DurationMs,
            ToDtos(result.Diagnostics)));
    }

    private static async Task<IResult> CompileHandler(
        LinqAuthoringRequest request,
        HttpContext http,
        IWorkflowLinqCompiler compiler,
        ICompiledAssemblyCache cache,
        IWorkflowTableCatalog catalog,
        IOptions<LinqEndpointsOptions> options,
        CancellationToken ct)
    {
        // 🔐 Trusted-author gate (Q2/Q15/D17) — a placeholder until real auth infra lands.
        var opts = options.Value;
        if (opts.RequireTrustedAuthorForCompile
            && !string.Equals(http.Request.Headers[opts.TrustedAuthorHeader], "true", StringComparison.OrdinalIgnoreCase))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var compileRequest = await BuildCompileRequest(request, catalog, ct).ConfigureAwait(false);
        var result = await compiler.CompileAsync(compileRequest, ct).ConfigureAwait(false);
        if (!result.Success || result.AssemblyBytes is null)
        {
            return Results.BadRequest(new ValidateResponse(false, ToDtos(result.Errors), ToDtos(result.Warnings)));
        }

        var key = cache.ComputeKey(
            compileRequest.DefinitionId,
            compileRequest.NodeId,
            compileRequest.UserCodeBody,
            LinqCodegen.SchemaVersion,
            compileRequest.SelectedTables);

        await cache.StoreAsync(key, result.AssemblyBytes, ct).ConfigureAwait(false);
        return Results.Ok(new CompileResponse(key));
    }

    private static async Task<IResult> CatalogImportHandler(
        string connectionId,
        ICatalogSchemaImporter importer,
        CancellationToken ct)
    {
        try
        {
            var imported = await importer.ImportAsync(connectionId, ct).ConfigureAwait(false);
            return Results.Ok(new CatalogImportResponse(imported));
        }
        catch (ConnectionNotFoundException)
        {
            return Results.NotFound(new { error = $"Connection '{connectionId}' not found." });
        }
    }

    private static async Task<IResult> CatalogListHandler(
        string connectionId,
        IWorkflowTableCatalog catalog,
        CancellationToken ct)
    {
        var tables = await catalog.ListAsync(connectionId, ct).ConfigureAwait(false);
        return Results.Ok(tables.Select(ToDto).ToArray());
    }

    private static async Task<IResult> CatalogUpsertHandler(
        string connectionId,
        string tableName,
        CatalogTableUpsertRequest request,
        IWorkflowTableCatalog catalog,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return Results.BadRequest(new { error = "Table name is required." });
        }

        var hasColumns = request.Columns is { Count: > 0 };
        var hasClrType = !string.IsNullOrWhiteSpace(request.ClrTypeName);
        if (!hasColumns && !hasClrType)
        {
            return Results.BadRequest(new { error = "Provide either columns (generated POCO) or a clrTypeName (plugin type)." });
        }

        var table = new WorkflowTableMetadata(
            connectionId,
            tableName,
            string.IsNullOrWhiteSpace(request.Schema) ? null : request.Schema,
            request.Columns?.Select(c => new WorkflowColumnMetadata(c.Name, c.DataType, c.Nullable)).ToList(),
            hasClrType ? request.ClrTypeName : null,
            hasClrType ? request.AssemblyName : null);

        await catalog.UpsertAsync(table, ct).ConfigureAwait(false);
        return Results.Ok(ToDto(table));
    }

    private static async Task<IResult> CatalogRemoveHandler(
        string connectionId,
        string tableName,
        IWorkflowTableCatalog catalog,
        CancellationToken ct)
    {
        var removed = await catalog.RemoveAsync(connectionId, tableName, ct).ConfigureAwait(false);
        return removed
            ? Results.NoContent()
            : Results.NotFound(new { error = $"Table '{tableName}' is not catalogued for connection '{connectionId}'." });
    }

    private static CatalogTableDto ToDto(WorkflowTableMetadata t)
        => new(
            t.TableName,
            t.Schema,
            t.Columns?.Select(c => new LinqColumnDto(c.Name, c.DataType, c.Nullable)).ToList(),
            t.ClrTypeName,
            t.AssemblyName);

    // ── Request → domain builders ──────────────────────────────────────────────────────────

    private static async Task<LinqCompileRequest> BuildCompileRequest(
        LinqAuthoringRequest request,
        IWorkflowTableCatalog catalog,
        CancellationToken ct)
    {
        var tables = await ResolveTables(request, catalog, ct).ConfigureAwait(false);
        var schema = BuildSchema(request.Inputs);
        return new LinqCompileRequest(
            request.DefinitionId ?? "definition",
            request.NodeId ?? "node",
            request.UserCode ?? string.Empty,
            tables,
            schema,
            request.StrictTypeMode);
    }

    private static async Task<IReadOnlyList<WorkflowTableMetadata>> ResolveTables(
        LinqAuthoringRequest request,
        IWorkflowTableCatalog catalog,
        CancellationToken ct)
    {
        // Inline tables win (self-contained requests); else resolve from the catalog by connection.
        if (request.Tables is { Count: > 0 })
        {
            return request.Tables.Select(t => new WorkflowTableMetadata(
                request.ConnectionId ?? "preview",
                t.TableName,
                t.Schema,
                t.Columns?.Select(c => new WorkflowColumnMetadata(c.Name, c.DataType, c.Nullable)).ToList(),
                t.ClrTypeName,
                t.AssemblyName)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(request.ConnectionId))
        {
            var all = await catalog.ListAsync(request.ConnectionId, ct).ConfigureAwait(false);
            if (request.TableNames is { Count: > 0 })
            {
                var set = new System.Collections.Generic.HashSet<string>(request.TableNames, StringComparer.OrdinalIgnoreCase);
                return all.Where(t => set.Contains(t.TableName)).ToList();
            }

            return all;
        }

        return Array.Empty<WorkflowTableMetadata>();
    }

    private static ModuleSchema BuildSchema(IReadOnlyList<LinqInputDto>? inputs)
    {
        if (inputs is null || inputs.Count == 0)
        {
            return new ModuleSchema(Arr<PortDefinition>.Empty, Arr<PortDefinition>.Empty, Arr<ModulePropertyDefinition>.Empty);
        }

        var props = inputs
            .Select(i => new ModulePropertyDefinition(i.Name, i.Name, MapType(i.Type), IsRequired: i.Required))
            .ToArray();
        return new ModuleSchema(Arr<PortDefinition>.Empty, Arr<PortDefinition>.Empty, Arr.create(props));
    }

    private static IReadOnlyDictionary<string, object?> CoerceInputs(LinqAuthoringRequest request)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (request.InputValues is null)
        {
            return result;
        }

        var typeByName = (request.Inputs ?? Enumerable.Empty<LinqInputDto>())
            .ToDictionary(i => i.Name, i => i.Type, StringComparer.Ordinal);

        foreach (var (name, raw) in request.InputValues)
        {
            var typeName = typeByName.TryGetValue(name, out var tn) ? tn : "object";
            result[name] = CoerceValue(raw, typeName);
        }

        return result;
    }

    private static object? CoerceValue(object? value, string typeName)
    {
        if (value is not JsonElement je)
        {
            return value;
        }

        if (je.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return typeName.ToLowerInvariant() switch
        {
            "string" => je.GetString(),
            "int" => je.GetInt32(),
            "long" => je.GetInt64(),
            "double" => je.GetDouble(),
            "decimal" => je.GetDecimal(),
            "bool" => je.GetBoolean(),
            "guid" => Guid.Parse(je.GetString()!),
            "datetime" => je.GetDateTime(),
            "datetimeoffset" => je.GetDateTimeOffset(),
            "timespan" => TimeSpan.Parse(je.GetString()!, System.Globalization.CultureInfo.InvariantCulture),
            _ => je.ToString(),
        };
    }

    private static Type MapType(string? typeName) => (typeName ?? string.Empty).ToLowerInvariant() switch
    {
        "string" => typeof(string),
        "int" => typeof(int),
        "long" => typeof(long),
        "double" => typeof(double),
        "decimal" => typeof(decimal),
        "bool" => typeof(bool),
        "guid" => typeof(Guid),
        "datetime" => typeof(DateTime),
        "datetimeoffset" => typeof(DateTimeOffset),
        "timespan" => typeof(TimeSpan),
        _ => typeof(object),
    };

    private static LinqDiagnosticDto[] ToDtos(IReadOnlyList<LinqDiagnostic> diagnostics)
        => diagnostics.Select(d => new LinqDiagnosticDto(d.Id, d.Severity.ToString(), d.Message, d.Line, d.Column)).ToArray();
}

/// <summary>⚙️ Options for the typed-linq endpoints (trusted-author gate)~ 🔐.</summary>
public sealed class LinqEndpointsOptions
{
    /// <summary>The config section (<c>Workflow:Database:Linq</c>).</summary>
    public const string SectionName = "Workflow:Database:Linq";

    /// <summary>Gets or sets whether <c>compile</c> requires the trusted-author header (default true — D17)~.</summary>
    public bool RequireTrustedAuthorForCompile { get; set; } = true;

    /// <summary>Gets or sets the header carrying the trusted-author signal (placeholder until real auth)~.</summary>
    public string TrustedAuthorHeader { get; set; } = "X-Trusted-Author";
}

/// <summary>📋 Authoring request for validate/preview/compile~.</summary>
/// <param name="DefinitionId">Owning definition id.</param>
/// <param name="NodeId">Node id.</param>
/// <param name="UserCode">The linq method body.</param>
/// <param name="ConnectionId">Named connection (for catalog table resolution + runtime).</param>
/// <param name="Tables">Inline table metadata (wins over catalog resolution).</param>
/// <param name="TableNames">Table names to resolve from the catalog for <paramref name="ConnectionId"/>.</param>
/// <param name="Inputs">Input property definitions (drive LinqInputs codegen).</param>
/// <param name="InputValues">Sample input values (preview only).</param>
/// <param name="StrictTypeMode">Reject non-allowlisted property/column types.</param>
public sealed record LinqAuthoringRequest(
    string? DefinitionId,
    string? NodeId,
    string? UserCode,
    string? ConnectionId,
    IReadOnlyList<LinqTableDto>? Tables,
    IReadOnlyList<string>? TableNames,
    IReadOnlyList<LinqInputDto>? Inputs,
    Dictionary<string, object?>? InputValues,
    bool StrictTypeMode = false);

/// <summary>📋 Inline table metadata for a request~.</summary>
/// <param name="TableName">Table name.</param>
/// <param name="Schema">Optional schema.</param>
/// <param name="Columns">Column metadata (for a generated POCO).</param>
/// <param name="ClrTypeName">Plugin CLR type name (for a plugin POCO).</param>
/// <param name="AssemblyName">Plugin assembly name.</param>
public sealed record LinqTableDto(
    string TableName,
    string? Schema,
    IReadOnlyList<LinqColumnDto>? Columns,
    string? ClrTypeName,
    string? AssemblyName);

/// <summary>📐 Inline column metadata~.</summary>
/// <param name="Name">Column name.</param>
/// <param name="DataType">Provider-reported data type.</param>
/// <param name="Nullable">Whether the column allows NULL.</param>
public sealed record LinqColumnDto(string Name, string DataType, bool Nullable);

/// <summary>🧬 Input property definition~.</summary>
/// <param name="Name">Property name.</param>
/// <param name="Type">Type token (string/int/long/double/decimal/bool/guid/datetime/datetimeoffset/timespan).</param>
/// <param name="Required">Whether the input is required.</param>
public sealed record LinqInputDto(string Name, string Type, bool Required);

/// <summary>🚦 API diagnostic DTO~.</summary>
/// <param name="Id">Diagnostic id.</param>
/// <param name="Severity">Severity (Error/Warning).</param>
/// <param name="Message">Message.</param>
/// <param name="Line">1-based line.</param>
/// <param name="Column">1-based column.</param>
public sealed record LinqDiagnosticDto(string Id, string Severity, string Message, int Line, int Column);

/// <summary>Response for validate~.</summary>
/// <param name="Success">Whether compilation succeeded.</param>
/// <param name="Errors">Error diagnostics.</param>
/// <param name="Warnings">Warning diagnostics.</param>
public sealed record ValidateResponse(bool Success, LinqDiagnosticDto[] Errors, LinqDiagnosticDto[] Warnings);

/// <summary>Response for preview~.</summary>
/// <param name="Success">Whether compile + run succeeded.</param>
/// <param name="Rows">Sample rows.</param>
/// <param name="RowCount">Row count.</param>
/// <param name="DurationMs">Elapsed time.</param>
/// <param name="Diagnostics">Diagnostics.</param>
public sealed record PreviewResponse(
    bool Success,
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? Rows,
    int? RowCount,
    long DurationMs,
    LinqDiagnosticDto[] Diagnostics);

/// <summary>Response for compile~.</summary>
/// <param name="CompiledAssemblyKey">The blob key of the cached compiled assembly.</param>
public sealed record CompileResponse(string CompiledAssemblyKey);

/// <summary>Response for catalog import~.</summary>
/// <param name="Imported">Number of tables imported.</param>
public sealed record CatalogImportResponse(int Imported);

/// <summary>📚 A catalogued table (list/upsert responses)~.</summary>
/// <param name="TableName">Table name.</param>
/// <param name="Schema">Optional schema.</param>
/// <param name="Columns">Column metadata (generated-POCO tables).</param>
/// <param name="ClrTypeName">Plugin CLR type name (plugin tables).</param>
/// <param name="AssemblyName">Plugin assembly name.</param>
public sealed record CatalogTableDto(
    string TableName,
    string? Schema,
    IReadOnlyList<LinqColumnDto>? Columns,
    string? ClrTypeName,
    string? AssemblyName);

/// <summary>📚 Manual table upsert request body~.</summary>
/// <param name="Schema">Optional schema.</param>
/// <param name="Columns">Column metadata (name/dataType/nullable) for a generated POCO.</param>
/// <param name="ClrTypeName">Plugin CLR type name (alternative to columns).</param>
/// <param name="AssemblyName">Plugin assembly name.</param>
public sealed record CatalogTableUpsertRequest(
    string? Schema,
    IReadOnlyList<LinqColumnDto>? Columns,
    string? ClrTypeName,
    string? AssemblyName);


