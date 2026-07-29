// <copyright file="LinqDtos.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Api.Dtos;

using System.Collections.Generic;

/// <summary>🧬 Linq Studio — authoring request for validate/preview/compile (mirrors <c>LinqAuthoringRequest</c>)~ ✨.</summary>
/// <param name="DefinitionId">Owning definition id.</param>
/// <param name="NodeId">Node id.</param>
/// <param name="UserCode">The linq method body.</param>
/// <param name="ConnectionId">Named connection (catalog resolution + runtime).</param>
/// <param name="Tables">Inline table metadata (wins over catalog resolution).</param>
/// <param name="TableNames">Table names resolved from the connection's catalog.</param>
/// <param name="Inputs">Typed input definitions.</param>
/// <param name="InputValues">Sample input values (preview only).</param>
/// <param name="StrictTypeMode">Reject non-allowlisted types.</param>
public sealed record LinqAuthoringRequestDto(
    string? DefinitionId,
    string? NodeId,
    string? UserCode,
    string? ConnectionId,
    List<LinqTableDto>? Tables,
    List<string>? TableNames,
    List<LinqInputDto>? Inputs,
    Dictionary<string, object?>? InputValues,
    bool StrictTypeMode = false);

/// <summary>📚 A catalogued/inline table (mirrors <c>CatalogTableDto</c>/<c>LinqTableDto</c>)~.</summary>
/// <param name="TableName">Table name.</param>
/// <param name="Schema">Optional schema.</param>
/// <param name="Columns">Column metadata (generated-POCO tables).</param>
/// <param name="ClrTypeName">Plugin CLR type name.</param>
/// <param name="AssemblyName">Plugin assembly name.</param>
public sealed record LinqTableDto(
    string TableName,
    string? Schema,
    List<LinqColumnDto>? Columns,
    string? ClrTypeName,
    string? AssemblyName);

/// <summary>📐 Column metadata~.</summary>
/// <param name="Name">Column name.</param>
/// <param name="DataType">Provider data type (integer/text/numeric/…).</param>
/// <param name="Nullable">Whether NULL is allowed.</param>
public sealed record LinqColumnDto(string Name, string DataType, bool Nullable);

/// <summary>🧬 Typed input definition~.</summary>
/// <param name="Name">Input name.</param>
/// <param name="Type">Type token (string/int/long/double/decimal/bool/guid/datetime/datetimeoffset/timespan).</param>
/// <param name="Required">Whether required.</param>
public sealed record LinqInputDto(string Name, string Type, bool Required);

/// <summary>🚦 A compile diagnostic~.</summary>
/// <param name="Id">Diagnostic id.</param>
/// <param name="Severity">Error/Warning.</param>
/// <param name="Message">Message.</param>
/// <param name="Line">1-based line.</param>
/// <param name="Column">1-based column.</param>
public sealed record LinqDiagnosticDto(string Id, string Severity, string Message, int Line, int Column);

/// <summary>✅ Validate response~.</summary>
/// <param name="Success">Compilation success.</param>
/// <param name="Errors">Errors.</param>
/// <param name="Warnings">Warnings.</param>
public sealed record LinqValidateResponseDto(bool Success, List<LinqDiagnosticDto> Errors, List<LinqDiagnosticDto> Warnings);

/// <summary>👀 Preview response (sample-data run)~.</summary>
/// <param name="Success">Compile + run success.</param>
/// <param name="Rows">Sample rows.</param>
/// <param name="RowCount">Row count.</param>
/// <param name="DurationMs">Elapsed ms.</param>
/// <param name="Diagnostics">Diagnostics.</param>
public sealed record LinqPreviewResponseDto(
    bool Success,
    List<Dictionary<string, object?>>? Rows,
    int? RowCount,
    long DurationMs,
    List<LinqDiagnosticDto> Diagnostics);

/// <summary>🔑 Compile response~.</summary>
/// <param name="CompiledAssemblyKey">The compiled assembly blob key.</param>
public sealed record LinqCompileResponseDto(string CompiledAssemblyKey);

/// <summary>📚 Manual table upsert body (mirrors <c>CatalogTableUpsertRequest</c>)~.</summary>
/// <param name="Schema">Optional schema.</param>
/// <param name="Columns">Columns for a generated POCO.</param>
/// <param name="ClrTypeName">Plugin CLR type (alternative).</param>
/// <param name="AssemblyName">Plugin assembly.</param>
public sealed record LinqTableUpsertDto(
    string? Schema,
    List<LinqColumnDto>? Columns,
    string? ClrTypeName,
    string? AssemblyName);

/// <summary>📥 Catalog import response~.</summary>
/// <param name="Imported">Tables imported.</param>
public sealed record LinqCatalogImportResponseDto(int Imported);

/// <summary>📇 A named database connection (mirrors <c>DbConnectionResponse</c>; connection string masked)~.</summary>
/// <param name="Id">Connection id.</param>
/// <param name="ProviderKey">Provider key (postgres/sqlite).</param>
/// <param name="ConnectionString">Masked connection string.</param>
/// <param name="DisplayName">Optional friendly name.</param>
/// <param name="Enabled">Whether usable.</param>
public sealed record DbConnectionDto(
    string Id,
    string ProviderKey,
    string ConnectionString,
    string? DisplayName,
    bool Enabled);

/// <summary>📇 Create/update body for a named connection (mirrors <c>DbConnectionRequest</c>)~.</summary>
/// <param name="Id">Unique connection id.</param>
/// <param name="ProviderKey">Provider key (postgres/sqlite).</param>
/// <param name="ConnectionString">The connection string (plaintext on write; masked in responses).</param>
/// <param name="DisplayName">Optional friendly name.</param>
/// <param name="Enabled">Whether the connection is usable (default true).</param>
public sealed record DbConnectionUpsertDto(
    string Id,
    string ProviderKey,
    string ConnectionString,
    string? DisplayName = null,
    bool? Enabled = null);
