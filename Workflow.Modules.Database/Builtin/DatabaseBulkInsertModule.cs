// <copyright file="DatabaseBulkInsertModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Builtin;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using LinqToDB.Data;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Internal;
using Workflow.Modules.Database.Transactions;

/// <summary>
/// 📊 Built-in database bulk-insert module (<c>builtin.database.bulkinsert</c>) — Phase 2.4.a.4~ ✨💖.
/// Inserts N row-dictionaries into a table as batched, parameterised multi-row INSERTs. Optionally
/// emits a provider-aware <c>RETURNING</c> clause to retrieve generated columns~ 🌸.
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.4.a.4 — uses <see cref="BatchInsertWriter"/> (hand-built multi-row INSERT)
/// rather than linq2db's generic <c>BulkCopy&lt;T&gt;</c>, which needs a compile-time entity type.
/// The typed <c>AsQueryable</c>/<c>InsertWithOutput</c> route lives in 2.4.b (Roslyn models) — see
/// Q14. All batches run in ONE transaction so a mid-run failure rolls back everything~ 🛡️.
/// </remarks>
public sealed class DatabaseBulkInsertModule : IWorkflowModule
{
    /// <inheritdoc/>
    public string ModuleId => "builtin.database.bulkinsert";

    /// <inheritdoc/>
    public string DisplayName => "Database Bulk Insert";

    /// <inheritdoc/>
    public string Category => "Database";

    /// <inheritdoc/>
    public string Description => "Inserts many rows efficiently via batched multi-row INSERTs, with optional RETURNING~ 📊✨";

    /// <inheritdoc/>
    public string Icon => "📊";

    /// <inheritdoc/>
    public Version Version => new(1, 0, 0);

    /// <inheritdoc/>
    public ModuleSchema Schema => new(
        Inputs: Arr<PortDefinition>.Empty,
        Outputs: Arr.create(
            new PortDefinition(
                Name: "insertedCount",
                DisplayName: "Inserted Count",
                DataType: typeof(int),
                Description: "Total rows inserted~ 🔢",
                IsRequired: false),
            new PortDefinition(
                Name: "outputRows",
                DisplayName: "Output Rows",
                DataType: typeof(IReadOnlyList<IReadOnlyDictionary<string, object?>>),
                Description: "Rows returned by the RETURNING clause (empty unless returningColumns is set)~ 📤",
                IsRequired: false),
            new PortDefinition(
                Name: "success",
                DisplayName: "Success",
                DataType: typeof(bool),
                Description: "True when all rows inserted and the transaction committed~ ✅",
                IsRequired: false),
            new PortDefinition(
                Name: "durationMs",
                DisplayName: "Duration (ms)",
                DataType: typeof(long),
                Description: "Elapsed time in milliseconds~ ⏱️",
                IsRequired: false)),
        Properties: Arr.create(
            new ModulePropertyDefinition(
                Name: "connectionId",
                DisplayName: "Connection Id",
                DataType: typeof(string),
                Description: "Named connection id (preferred). Mutually exclusive with connectionString~ 📇",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text),
            new ModulePropertyDefinition(
                Name: "connectionString",
                DisplayName: "Connection String",
                DataType: typeof(string),
                Description: "Raw connection string (escape hatch). Requires 'provider' when set~ 🔓",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.ConnectionString),
            new ModulePropertyDefinition(
                Name: "provider",
                DisplayName: "Provider",
                DataType: typeof(string),
                Description: "Provider key ('postgres'/'sqlite') — required only with connectionString~ 🗂️",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Dropdown),
            new ModulePropertyDefinition(
                Name: "tableName",
                DisplayName: "Table Name",
                DataType: typeof(string),
                Description: "Target table — fully-qualified preferred (e.g. 'public.orders')~ 🎯",
                IsRequired: true,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text),
            new ModulePropertyDefinition(
                Name: "data",
                DisplayName: "Data",
                DataType: typeof(IReadOnlyList<IReadOnlyDictionary<string, object?>>),
                Description: "Array of row objects (column→value)~ 📊",
                IsRequired: true,
                DefaultValue: null,
                EditorType: PropertyEditorType.Json),
            new ModulePropertyDefinition(
                Name: "columnMapping",
                DisplayName: "Column Mapping",
                DataType: typeof(Dictionary<string, string>),
                Description: "Optional input-key → DB-column map; identity by default~ 🗺️",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Json),
            new ModulePropertyDefinition(
                Name: "returningColumns",
                DisplayName: "Returning Columns",
                DataType: typeof(IReadOnlyList<string>),
                Description: "Optional columns to emit via RETURNING (e.g. ['id']) → outputRows~ 📤",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Json),
            new ModulePropertyDefinition(
                Name: "batchSize",
                DisplayName: "Batch Size",
                DataType: typeof(int),
                Description: "Requested rows per INSERT (clamped by the provider parameter limit; default 1000)~ 📦",
                IsRequired: false,
                DefaultValue: 1000,
                EditorType: PropertyEditorType.Number),
            new ModulePropertyDefinition(
                Name: "timeoutSeconds",
                DisplayName: "Timeout (seconds)",
                DataType: typeof(int),
                Description: "Command timeout in seconds (default 120)~ ⏱️",
                IsRequired: false,
                DefaultValue: 120,
                EditorType: PropertyEditorType.Number)));

    /// <inheritdoc/>
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        var errors = new List<ValidationError>();

        DbModuleSupport.ValidateConnectionSource(configuration, errors);

        if (string.IsNullOrWhiteSpace(DbModuleSupport.GetString(configuration, "tableName")))
        {
            errors.Add(new ValidationError(
                "DB_TABLENAME_REQUIRED",
                "'tableName' is required and must be non-empty~ 💔",
                PropertyName: "tableName"));
        }

        if (!configuration.TryGetValue("data", out var data) || data is null)
        {
            errors.Add(new ValidationError(
                "DB_DATA_REQUIRED",
                "'data' is required (an array of row objects)~ 💔",
                PropertyName: "data"));
        }

        var batchSize = DbModuleSupport.TryParseInt(configuration, "batchSize");
        if (batchSize.HasValue && batchSize.Value <= 0)
        {
            errors.Add(new ValidationError(
                "DB_BATCHSIZE_INVALID",
                $"batchSize must be > 0 (got {batchSize.Value})~ 💔",
                PropertyName: "batchSize"));
        }

        var timeout = DbModuleSupport.TryParseInt(configuration, "timeoutSeconds");
        if (timeout.HasValue && timeout.Value <= 0)
        {
            errors.Add(new ValidationError(
                "DB_TIMEOUT_INVALID",
                $"timeoutSeconds must be > 0 (got {timeout.Value})~ 💔",
                PropertyName: "timeoutSeconds"));
        }

        return errors.Count > 0 ? ValidationResult.Failure(errors.ToArray()) : ValidationResult.Success();
    }

    /// <inheritdoc/>
    public async Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Services.GetService(typeof(IDbConnectionFactory)) is not IDbConnectionFactory factory)
        {
            return ModuleResult.Fail(
                "IDbConnectionFactory not registered in DI. Call services.AddDatabaseModules() at host startup~ 💔");
        }

        var validation = this.ValidateConfiguration(context.Properties);
        if (!validation.IsValid)
        {
            return ModuleResult.Fail($"Invalid configuration: {string.Join("; ", validation.Errors)}~ 💔");
        }

        var tableName = DbModuleSupport.GetString(context.Properties, "tableName")!;
        var timeoutSeconds = DbModuleSupport.TryParseInt(context.Properties, "timeoutSeconds") ?? 120;
        var batchSize = DbModuleSupport.TryParseInt(context.Properties, "batchSize") ?? 1000;

        var data = CoerceData(context.Properties.TryGetValue("data", out var d) ? d : null);
        if (data is null)
        {
            return ModuleResult.Fail("'data' must be an array of row objects (column→value)~ 💔");
        }

        var columnMapping = CoerceMapping(context.Properties.TryGetValue("columnMapping", out var cm) ? cm : null);
        var returningColumns = CoerceStringList(context.Properties.TryGetValue("returningColumns", out var rc) ? rc : null);

        var sw = Stopwatch.StartNew();

        if (data.Count == 0)
        {
            sw.Stop();
            return ModuleResult.Ok(
                new Dictionary<string, object?>
                {
                    ["insertedCount"] = 0,
                    ["outputRows"] = Array.Empty<IReadOnlyDictionary<string, object?>>(),
                    ["success"] = true,
                    ["durationMs"] = sw.ElapsedMilliseconds,
                },
                ExecutionMetrics.FromDuration(sw.Elapsed));
        }

        DataConnection db;
        try
        {
            db = await DbModuleSupport.CreateConnectionAsync(factory, context.Properties, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ConnectionNotFoundException ex)
        {
            sw.Stop();
            return ModuleResult.Fail($"Connection '{ex.ConnectionId}' not found~ 💔", ex);
        }
        catch (UnknownProviderException ex)
        {
            sw.Stop();
            return ModuleResult.Fail($"Unknown provider '{ex.ProviderKey}'~ 💔", ex);
        }

        IDbTransactionScope? scope = null;
        try
        {
            db.CommandTimeout = timeoutSeconds;
            scope = await DefaultDbTransactionScope
                .CreateAsync(db, DefaultIsolation(db.DataProvider.Name), cancellationToken)
                .ConfigureAwait(false);

            var result = BatchInsertWriter.Write(scope.Connection, tableName, data, columnMapping, batchSize, returningColumns);

            await scope.CommitAsync(cancellationToken).ConfigureAwait(false);
            sw.Stop();

            return ModuleResult.Ok(
                new Dictionary<string, object?>
                {
                    ["insertedCount"] = result.InsertedCount,
                    ["outputRows"] = result.OutputRows,
                    ["success"] = true,
                    ["durationMs"] = sw.ElapsedMilliseconds,
                },
                ExecutionMetrics.FromDuration(sw.Elapsed));
        }
        catch (BatchInsertWriter.BulkRowBindException ex)
        {
            sw.Stop();
            return ModuleResult.Fail($"Bulk insert failed binding row {ex.RowIndex}: {ex.Message}~ 💔", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning disable CA1031 // Provider-level SQL errors are intentionally caught wide and surfaced as a clean Fail (transaction auto-rolls-back on dispose)~ 🌸
        {
            sw.Stop();
            return ModuleResult.Fail($"Bulk insert failed: {DbErrorContext.Describe(ex)}~ 💔", ex);
        }
#pragma warning restore CA1031
        finally
        {
            if (scope is not null)
            {
                await scope.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                await db.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>Default isolation for the bulk transaction — SQLite only accepts Serializable/ReadUncommitted~ 🔒.</summary>
    private static IsolationLevel DefaultIsolation(string providerName)
        => providerName.Contains("SQLite", StringComparison.OrdinalIgnoreCase)
            ? IsolationLevel.Serializable
            : IsolationLevel.ReadCommitted;

    /// <summary>Coerces the loosely-typed <c>data</c> property into a list of row dictionaries~ 🧩.</summary>
    private static IReadOnlyList<IReadOnlyDictionary<string, object?>>? CoerceData(object? raw)
    {
        if (raw is null || raw is string || raw is not IEnumerable enumerable)
        {
            return null;
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var item in enumerable)
        {
            var map = SqlParameterBinder.Normalize(item);
            if (map is null)
            {
                return null;
            }

            rows.Add(map);
        }

        return rows;
    }

    /// <summary>Coerces the loosely-typed <c>columnMapping</c> property into a string→string map~ 🗺️.</summary>
    private static IReadOnlyDictionary<string, string>? CoerceMapping(object? raw)
    {
        var norm = SqlParameterBinder.Normalize(raw);
        if (norm is null)
        {
            return null;
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in norm)
        {
            map[kv.Key] = kv.Value?.ToString() ?? kv.Key;
        }

        return map;
    }

    /// <summary>Coerces the loosely-typed <c>returningColumns</c> property into a string list~ 📤.</summary>
    private static IReadOnlyList<string>? CoerceStringList(object? raw)
    {
        switch (raw)
        {
            case null:
                return null;
            case string s:
                return new[] { s };
            case IEnumerable enumerable:
            {
                var list = new List<string>();
                foreach (var item in enumerable)
                {
                    if (item is not null)
                    {
                        list.Add(item.ToString()!);
                    }
                }

                return list;
            }

            default:
                return null;
        }
    }
}
