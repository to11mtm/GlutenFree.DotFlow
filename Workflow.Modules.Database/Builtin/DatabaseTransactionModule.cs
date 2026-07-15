// <copyright file="DatabaseTransactionModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Builtin;

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using LinqToDB.Data;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Internal;
using Workflow.Modules.Database.Models;
using Workflow.Modules.Database.Transactions;

/// <summary>
/// 💼 Built-in database transaction module (<c>builtin.database.transaction</c>) — Phase 2.4.a.3~ ✨💖.
/// Runs an ordered list of SQL operations atomically: commit iff every op succeeds, else roll back
/// with the failing op's index. Supports single-mode (<c>parameters</c>) and batch-mode
/// (<c>parameterSets</c>) ops (mutually exclusive)~ 🛡️.
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.4.a.3 — a SQL error is a CLEAN failure (returns
/// <c>ModuleResult.Ok(success=false, error=…)</c> the engine routes, per Diagram A). Only
/// infra failures (missing factory, unknown connection) return <see cref="ModuleResult.Fail"/>.
/// Conditional aborts compose at the workflow level (condition + throw + trycatch, D11); savepoints
/// land in 2.4.a.P2~ 🌸.
/// </remarks>
public sealed class DatabaseTransactionModule : IWorkflowModule
{
    private static readonly IReadOnlyDictionary<string, IsolationLevel> IsolationLevels =
        new Dictionary<string, IsolationLevel>(StringComparer.OrdinalIgnoreCase)
        {
            ["ReadUncommitted"] = IsolationLevel.ReadUncommitted,
            ["ReadCommitted"] = IsolationLevel.ReadCommitted,
            ["RepeatableRead"] = IsolationLevel.RepeatableRead,
            ["Serializable"] = IsolationLevel.Serializable,
            ["Snapshot"] = IsolationLevel.Snapshot,
        };

    /// <inheritdoc/>
    public string ModuleId => "builtin.database.transaction";

    /// <inheritdoc/>
    public string DisplayName => "Database Transaction";

    /// <inheritdoc/>
    public string Category => "Database";

    /// <inheritdoc/>
    public string Description => "Runs a sequence of SQL operations atomically — commit on success, rollback on any failure~ 💼✨";

    /// <inheritdoc/>
    public string Icon => "💼";

    /// <inheritdoc/>
    public Version Version => new(1, 0, 0);

    /// <inheritdoc/>
    public ModuleSchema Schema => new(
        Inputs: Arr<PortDefinition>.Empty,
        Outputs: Arr.create(
            new PortDefinition(
                Name: "success",
                DisplayName: "Success",
                DataType: typeof(bool),
                Description: "True only if all ops succeeded and the commit succeeded~ ✅",
                IsRequired: false),
            new PortDefinition(
                Name: "results",
                DisplayName: "Results",
                DataType: typeof(IReadOnlyList<DbOperationResult>),
                Description: "Per-op results (affectedRows + optional lastInsertId)~ 📊",
                IsRequired: false),
            new PortDefinition(
                Name: "error",
                DisplayName: "Error",
                DataType: typeof(DbOperationError),
                Description: "Failure context (operationIndex, sqlState, message, batchRowIndex) — null on success~ 🚨",
                IsRequired: false),
            new PortDefinition(
                Name: "durationMs",
                DisplayName: "Duration (ms)",
                DataType: typeof(long),
                Description: "Transaction round-trip elapsed time in milliseconds~ ⏱️",
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
                Name: "operations",
                DisplayName: "Operations",
                DataType: typeof(IReadOnlyList<DbOperationSpec>),
                Description: "Ordered ops — each { sql, parameters? | parameterSets?, expectLastInsertId? }~ 💼",
                IsRequired: true,
                DefaultValue: null,
                EditorType: PropertyEditorType.Json),
            new ModulePropertyDefinition(
                Name: "isolationLevel",
                DisplayName: "Isolation Level",
                DataType: typeof(string),
                Description: "ReadUncommitted/ReadCommitted/RepeatableRead/Serializable/Snapshot (default ReadCommitted; clamped per provider)~ 🔒",
                IsRequired: false,
                DefaultValue: "ReadCommitted",
                EditorType: PropertyEditorType.Dropdown),
            new ModulePropertyDefinition(
                Name: "timeoutSeconds",
                DisplayName: "Timeout (seconds)",
                DataType: typeof(int),
                Description: "Command timeout in seconds (default 60)~ ⏱️",
                IsRequired: false,
                DefaultValue: 60,
                EditorType: PropertyEditorType.Number)));

    /// <inheritdoc/>
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        var errors = new List<ValidationError>();

        DbModuleSupport.ValidateConnectionSource(configuration, errors);

        // operations must parse (also enforces the parameters/parameterSets exclusivity + no-savepoints)~ 🧩
        var hasOps = configuration.TryGetValue("operations", out var opsRaw) && opsRaw is not null;
        if (!hasOps)
        {
            errors.Add(new ValidationError(
                "DB_OPERATIONS_REQUIRED",
                "'operations' is required and must be a non-empty list~ 💔",
                PropertyName: "operations"));
        }
        else
        {
            try
            {
                DbOperationParser.Parse(opsRaw);
            }
            catch (DbOperationParseException ex)
            {
                errors.Add(new ValidationError(
                    "DB_OPERATIONS_INVALID",
                    ex.Message,
                    PropertyName: "operations"));
            }
        }

        // isolationLevel must be a known value when supplied~ 🔒
        var isolation = DbModuleSupport.GetString(configuration, "isolationLevel");
        if (!string.IsNullOrWhiteSpace(isolation) && !IsolationLevels.ContainsKey(isolation))
        {
            errors.Add(new ValidationError(
                "DB_ISOLATION_INVALID",
                $"Unknown isolationLevel '{isolation}'. Valid: {string.Join(", ", IsolationLevels.Keys)}~ 💔",
                PropertyName: "isolationLevel"));
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

        IReadOnlyList<DbOperationSpec> operations;
        try
        {
            operations = DbOperationParser.Parse(context.Properties.TryGetValue("operations", out var o) ? o : null);
        }
        catch (DbOperationParseException ex)
        {
            return ModuleResult.Fail($"Invalid operations: {ex.Message}~ 💔", ex);
        }

        var timeoutSeconds = DbModuleSupport.TryParseInt(context.Properties, "timeoutSeconds") ?? 60;
        var requestedIsolation = ResolveIsolation(DbModuleSupport.GetString(context.Properties, "isolationLevel"));

        var sw = Stopwatch.StartNew();

        // Empty ops → clean no-op success~ 🌸
        if (operations.Count == 0)
        {
            sw.Stop();
            return ModuleResult.Ok(
                new Dictionary<string, object?>
                {
                    ["success"] = true,
                    ["results"] = Array.Empty<DbOperationResult>(),
                    ["error"] = null,
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
            var isolation = ClampIsolationForProvider(db.DataProvider.Name, requestedIsolation, context);
            scope = await DefaultDbTransactionScope.CreateAsync(db, isolation, cancellationToken).ConfigureAwait(false);

            var results = new List<DbOperationResult>(operations.Count);

            for (var i = 0; i < operations.Count; i++)
            {
                var op = operations[i];
                var (result, error) = RunOperation(scope.Connection, op, i, context);

                if (error is not null)
                {
                    await scope.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    sw.Stop();
                    return ModuleResult.Ok(
                        new Dictionary<string, object?>
                        {
                            ["success"] = false,
                            ["results"] = results,
                            ["error"] = error,
                            ["durationMs"] = sw.ElapsedMilliseconds,
                        },
                        ExecutionMetrics.FromDuration(sw.Elapsed));
                }

                results.Add(result!);
            }

            await scope.CommitAsync(cancellationToken).ConfigureAwait(false);
            sw.Stop();

            return ModuleResult.Ok(
                new Dictionary<string, object?>
                {
                    ["success"] = true,
                    ["results"] = results,
                    ["error"] = null,
                    ["durationMs"] = sw.ElapsedMilliseconds,
                },
                ExecutionMetrics.FromDuration(sw.Elapsed));
        }
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

    /// <summary>
    /// Runs one operation (single- or batch-mode). Returns a result on success, or an error
    /// record on the first SQL failure (which the caller turns into a rollback + clean failure)~ 🌸.
    /// </summary>
    private static (DbOperationResult? Result, DbOperationError? Error) RunOperation(
        DataConnection db,
        DbOperationSpec op,
        int index,
        ModuleExecutionContext context)
    {
        if (op.IsBatch)
        {
            var sets = op.ParameterSets!;
            var totalAffected = 0;
            long? lastId = null;

            for (var setIndex = 0; setIndex < sets.Count; setIndex++)
            {
                DataParameter[] boundParams;
                try
                {
                    boundParams = SqlParameterBinder.Bind(sets[setIndex]);
                }
                catch (SqlParameterBindingException ex)
                {
                    return (null, new DbOperationError(index, null, ex.Message, setIndex));
                }

                try
                {
                    var (affected, id) = DbSingleOpExecutor.Execute(db, op.Sql, boundParams, op.ExpectLastInsertId, context.Logger);
                    totalAffected += affected;
                    if (id is not null)
                    {
                        lastId = id;
                    }
                }
                catch (Exception ex) when (IsDataError(ex))
                {
                    return (null, new DbOperationError(index, DbErrorContext.TryGetSqlState(ex), DbErrorContext.Describe(ex), setIndex));
                }
            }

            return (new DbOperationResult(totalAffected, lastId, IsBatchOp: true, BatchExecutionCount: sets.Count), null);
        }

        // Single mode~
        DataParameter[] parameters;
        try
        {
            parameters = SqlParameterBinder.Bind(op.Parameters);
        }
        catch (SqlParameterBindingException ex)
        {
            return (null, new DbOperationError(index, null, ex.Message, null));
        }

        try
        {
            var (affected, id) = DbSingleOpExecutor.Execute(db, op.Sql, parameters, op.ExpectLastInsertId, context.Logger);
            return (new DbOperationResult(affected, id, IsBatchOp: false, BatchExecutionCount: 0), null);
        }
        catch (Exception ex) when (IsDataError(ex))
        {
            return (null, new DbOperationError(index, DbErrorContext.TryGetSqlState(ex), DbErrorContext.Describe(ex), null));
        }
    }

    /// <summary>Maps the isolation-level string to <see cref="IsolationLevel"/> (default ReadCommitted)~ 🔒.</summary>
    private static IsolationLevel ResolveIsolation(string? isolation)
        => isolation is not null && IsolationLevels.TryGetValue(isolation, out var level)
            ? level
            : IsolationLevel.ReadCommitted;

    /// <summary>
    /// Clamps the requested isolation to what the provider actually supports~ 🛡️.
    /// SQLite (Microsoft.Data.Sqlite) only accepts Serializable + ReadUncommitted; everything else
    /// throws, so we clamp. Postgres has no Snapshot → Serializable~.
    /// </summary>
    private static IsolationLevel ClampIsolationForProvider(
        string providerName,
        IsolationLevel requested,
        ModuleExecutionContext context)
    {
        if (providerName.Contains("SQLite", StringComparison.OrdinalIgnoreCase))
        {
            var clamped = requested == IsolationLevel.ReadUncommitted
                ? IsolationLevel.ReadUncommitted
                : IsolationLevel.Serializable;

            if (clamped != requested)
            {
                context.Logger.LogDebug(
                    "🔒 SQLite supports only Serializable/ReadUncommitted — clamping requested {Requested} to {Clamped}~",
                    requested,
                    clamped);
            }

            return clamped;
        }

        // Postgres (and others): Snapshot isn't supported → Serializable~
        return requested == IsolationLevel.Snapshot ? IsolationLevel.Serializable : requested;
    }

    /// <summary>
    /// True for provider-level data errors we convert into a clean <c>success=false</c> (vs. infra
    /// failures that should surface as an exception/Fail). We treat any non-cancellation exception
    /// raised during op execution as a data error~ 🌸.
    /// </summary>
    private static bool IsDataError(Exception ex)
        => ex is not OperationCanceledException;
}
