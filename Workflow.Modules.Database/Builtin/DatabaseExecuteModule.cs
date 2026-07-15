// <copyright file="DatabaseExecuteModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Builtin;

using System;
using System.Collections.Generic;
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

/// <summary>
/// ✏️ Built-in database execute module (<c>builtin.database.execute</c>) — Phase 2.4.a.2~ ✨💖.
/// INSERT/UPDATE/DELETE: returns the affected-row count plus an optional <c>lastInsertId</c>.
/// Parameterised only (D7); provider-aware last-insert-id resolution (SQLite vs Postgres)~ 🌸.
/// </summary>
/// <remarks>
/// CopilotNote: Parameterless-constructable (reflection discovery). Resolves
/// <see cref="IDbConnectionFactory"/> from <see cref="ModuleExecutionContext.Services"/> and
/// shares config/connection plumbing with the query module via <see cref="DbModuleSupport"/>~ 🧰.
/// </remarks>
public sealed class DatabaseExecuteModule : IWorkflowModule
{
    /// <inheritdoc/>
    public string ModuleId => "builtin.database.execute";

    /// <inheritdoc/>
    public string DisplayName => "Database Execute";

    /// <inheritdoc/>
    public string Category => "Database";

    /// <inheritdoc/>
    public string Description => "Runs a parameterised INSERT/UPDATE/DELETE and returns the affected row count~ ✏️✨";

    /// <inheritdoc/>
    public string Icon => "✏️";

    /// <inheritdoc/>
    public Version Version => new(1, 0, 0);

    /// <inheritdoc/>
    public ModuleSchema Schema => new(
        Inputs: Arr<PortDefinition>.Empty,
        Outputs: Arr.create(
            new PortDefinition(
                Name: "affectedRows",
                DisplayName: "Affected Rows",
                DataType: typeof(int),
                Description: "Number of rows inserted/updated/deleted~ 🔢",
                IsRequired: false),
            new PortDefinition(
                Name: "lastInsertId",
                DisplayName: "Last Insert Id",
                DataType: typeof(long?),
                Description: "Auto-generated id when expectsLastInsertId is set (SQLite rowid / Postgres RETURNING)~ 🆔",
                IsRequired: false),
            new PortDefinition(
                Name: "success",
                DisplayName: "Success",
                DataType: typeof(bool),
                Description: "True when the command executed without error~ ✅",
                IsRequired: false),
            new PortDefinition(
                Name: "durationMs",
                DisplayName: "Duration (ms)",
                DataType: typeof(long),
                Description: "Command round-trip elapsed time in milliseconds~ ⏱️",
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
                Name: "command",
                DisplayName: "Command (SQL)",
                DataType: typeof(string),
                Description: "Verbatim INSERT/UPDATE/DELETE SQL. NOT template-expanded (D7)~ ✏️",
                IsRequired: true,
                DefaultValue: null,
                EditorType: PropertyEditorType.Code),
            new ModulePropertyDefinition(
                Name: "parameters",
                DisplayName: "Parameters",
                DataType: typeof(Dictionary<string, object?>),
                Description: "Named SQL parameters (name→value)~ 🧷",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Json),
            new ModulePropertyDefinition(
                Name: "timeoutSeconds",
                DisplayName: "Timeout (seconds)",
                DataType: typeof(int),
                Description: "Command timeout in seconds (default 30)~ ⏱️",
                IsRequired: false,
                DefaultValue: 30,
                EditorType: PropertyEditorType.Number),
            new ModulePropertyDefinition(
                Name: "expectsLastInsertId",
                DisplayName: "Expects Last Insert Id",
                DataType: typeof(bool),
                Description: "When true, resolves lastInsertId — SQLite via last_insert_rowid(); Postgres via a user-supplied RETURNING clause~ 🆔",
                IsRequired: false,
                DefaultValue: false,
                EditorType: PropertyEditorType.Boolean)));

    /// <inheritdoc/>
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        var errors = new List<ValidationError>();

        // Connection-source rule (connectionId XOR connectionString+provider) — shared (D3)~ 🔀
        DbModuleSupport.ValidateConnectionSource(configuration, errors);

        // Command non-empty~ ✏️
        if (string.IsNullOrWhiteSpace(DbModuleSupport.GetString(configuration, "command")))
        {
            errors.Add(new ValidationError(
                "DB_COMMAND_REQUIRED",
                "'command' is required and must be non-empty~ 💔",
                PropertyName: "command"));
        }

        // Timeout must be positive when supplied~ ⏱️
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

        // 1️⃣ Resolve the shared connection factory from DI~ 🧠
        if (context.Services.GetService(typeof(IDbConnectionFactory)) is not IDbConnectionFactory factory)
        {
            return ModuleResult.Fail(
                "IDbConnectionFactory not registered in DI. Call services.AddDatabaseModules() at host startup~ 💔");
        }

        // 2️⃣ Validate config~ 📋
        var validation = this.ValidateConfiguration(context.Properties);
        if (!validation.IsValid)
        {
            return ModuleResult.Fail(
                $"Invalid configuration: {string.Join("; ", validation.Errors)}~ 💔");
        }

        var command = DbModuleSupport.GetString(context.Properties, "command")!;
        var timeoutSeconds = DbModuleSupport.TryParseInt(context.Properties, "timeoutSeconds") ?? 30;
        var expectsLastInsertId = DbModuleSupport.TryParseBool(context.Properties, "expectsLastInsertId") ?? false;

        DataParameter[] parameters;
        try
        {
            parameters = SqlParameterBinder.Bind(
                SqlParameterBinder.Normalize(context.Properties.TryGetValue("parameters", out var p) ? p : null));
        }
        catch (SqlParameterBindingException ex)
        {
            return ModuleResult.Fail(ex.Message, ex);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var db = await DbModuleSupport
                .CreateConnectionAsync(factory, context.Properties, cancellationToken)
                .ConfigureAwait(false);

            db.CommandTimeout = timeoutSeconds;

            var (affectedRows, lastInsertId) = Execute(db, command, parameters, expectsLastInsertId, context.Logger);
            sw.Stop();

            var outputs = new Dictionary<string, object?>
            {
                ["affectedRows"] = affectedRows,
                ["lastInsertId"] = lastInsertId,
                ["success"] = true,
                ["durationMs"] = sw.ElapsedMilliseconds,
            };

            return ModuleResult.Ok(outputs, ExecutionMetrics.FromDuration(sw.Elapsed));
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
        catch (Exception ex)
#pragma warning disable CA1031 // Provider-level SQL errors (Npgsql/Sqlite) are intentionally caught wide and surfaced as a clean Fail with constraint context~ 🌸
        {
            sw.Stop();
            return ModuleResult.Fail($"Database execute failed: {DbErrorContext.Describe(ex)}~ 💔", ex);
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Runs the command and, when requested, resolves the last-insert id in a provider-aware way~ 🆔.
    /// </summary>
    private static (int AffectedRows, long? LastInsertId) Execute(
        DataConnection db,
        string command,
        DataParameter[] parameters,
        bool expectsLastInsertId,
        ILogger logger)
    {
        var providerName = db.DataProvider.Name;
        var isPostgres = providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase);

        // 🐘 Postgres: the id (if any) rides on a user-supplied RETURNING clause (Q12 — no auto-rewrite).
        // We read it via a reader so both the returned value AND the row count are captured.
        if (expectsLastInsertId && isPostgres)
        {
            long? returningId = null;
            var rowCount = 0;
            using var reader = db.ExecuteReader(command, parameters);
            var r = reader.Reader!;
            while (r.Read())
            {
                rowCount++;
                if (returningId is null && r.FieldCount > 0)
                {
                    var value = r.GetValue(0);
                    if (value is not DBNull)
                    {
                        returningId = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
            }

            if (returningId is null)
            {
                logger.LogWarning(
                    "⚠️ expectsLastInsertId was set but the Postgres command returned no RETURNING value — lastInsertId is null. Add 'RETURNING id' to your INSERT~ 🌸");
            }

            return (rowCount, returningId);
        }

        // Everything else: affected-row count from a plain Execute~
        var affected = db.Execute(command, parameters);

        if (!expectsLastInsertId)
        {
            return (affected, null);
        }

        // 🪶 SQLite: last_insert_rowid() on the SAME open connection~
        long? lastId = db.Execute<long?>("SELECT last_insert_rowid()");
        return (affected, lastId);
    }
}
