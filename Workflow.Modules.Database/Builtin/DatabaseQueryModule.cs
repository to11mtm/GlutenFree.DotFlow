// <copyright file="DatabaseQueryModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

using LinqToDB;

namespace Workflow.Modules.Database.Builtin;

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using LinqToDB.Data;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Internal;

/// <summary>
/// 🔍 Built-in database query module (<c>builtin.database.query</c>) — Phase 2.4.a.1~ ✨💖.
/// SELECT-only: returns materialised rows, column names, and a row count. Never string-concatenates
/// SQL (D7) — parameters bind through <see cref="SqlParameterBinder"/>. Outputs are always fully
/// materialised (D8) — no open readers or <c>IQueryable</c> escape the module~ 🌸.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: The module is parameterless-constructable so <c>ModuleDiscovery</c> can reflectively
/// instantiate it. It resolves <see cref="IDbConnectionFactory"/> lazily from
/// <see cref="ModuleExecutionContext.Services"/> — the host wires the family via
/// <c>services.AddDatabaseModules()</c> (D2/D14; host-side assembly scan lands in 2.4.a.5)~ 🧠.
/// </para>
/// </remarks>
public sealed class DatabaseQueryModule : IStreamingWorkflowModule
{
    /// <inheritdoc/>
    public string ModuleId => "builtin.database.query";

    /// <inheritdoc/>
    public string DisplayName => "Database Query";

    /// <inheritdoc/>
    public string Category => "Database";

    /// <inheritdoc/>
    public string Description => "Runs a parameterised SELECT and returns the rows, columns, and row count~ 🔍✨";

    /// <inheritdoc/>
    public string Icon => "🔍";

    /// <inheritdoc/>
    public Version Version => new(1, 0, 0);

    /// <inheritdoc/>
    public ModuleSchema Schema => new(
        Inputs: Arr<PortDefinition>.Empty,
        Outputs: Arr.create(
            new PortDefinition(
                Name: "rows",
                DisplayName: "Rows",
                DataType: typeof(IReadOnlyList<IReadOnlyDictionary<string, object?>>),
                Description: "Materialised result rows (each a column→value dictionary)~ 📊",
                IsRequired: false),
            new PortDefinition(
                Name: "rowCount",
                DisplayName: "Row Count",
                DataType: typeof(int),
                Description: "Number of rows returned~ 🔢",
                IsRequired: false),
            new PortDefinition(
                Name: "columns",
                DisplayName: "Columns",
                DataType: typeof(IReadOnlyList<string>),
                Description: "Ordered column names from the result set~ 🏷️",
                IsRequired: false),
            new PortDefinition(
                Name: "success",
                DisplayName: "Success",
                DataType: typeof(bool),
                Description: "True when the query executed without error~ ✅",
                IsRequired: false),
            new PortDefinition(
                Name: "durationMs",
                DisplayName: "Duration (ms)",
                DataType: typeof(long),
                Description: "Query round-trip elapsed time in milliseconds~ ⏱️",
                IsRequired: false),
            PortDefinition.CreateStreaming(
                "items",
                isRequired: false,
                description: "Result rows as a stream — reads with bounded memory instead of materialising every row~ 🌊")),
        Properties: Arr.create(
            new ModulePropertyDefinition(
                Name: "connectionId",
                DisplayName: "Connection Id",
                DataType: typeof(string),
                Description: "Named connection id (preferred). Mutually exclusive with connectionString~ 📇",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text,
                SupportsTemplates: true),
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
                EditorType: PropertyEditorType.Dropdown,
                AllowedValues: Arr.create<object>("postgres", "sqlite")),
            new ModulePropertyDefinition(
                Name: "query",
                DisplayName: "Query (SQL)",
                DataType: typeof(string),
                Description: "Verbatim SELECT SQL. NOT template-expanded (D7) — use parameters for values~ 🔍",
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
                Name: "commandType",
                DisplayName: "Command Type",
                DataType: typeof(string),
                Description: "'text' (default). 'storedProcedure' is deferred to 2.4.a.P1~ 🎛️",
                IsRequired: false,
                DefaultValue: "text",
                EditorType: PropertyEditorType.Dropdown,
                AllowedValues: Arr.create<object>("text"))));

    /// <inheritdoc/>
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        var errors = new List<ValidationError>();

        // Connection-source rule (connectionId XOR connectionString+provider) — shared (D3)~ 🔀
        DbModuleSupport.ValidateConnectionSource(configuration, errors);

        // Query non-empty~ 🔍
        if (string.IsNullOrWhiteSpace(DbModuleSupport.GetString(configuration, "query")))
        {
            errors.Add(new ValidationError(
                "DB_QUERY_REQUIRED",
                "'query' is required and must be non-empty~ 💔",
                PropertyName: "query"));
        }

        // storedProcedure deferred to 2.4.a.P1~ 🎛️
        var commandType = DbModuleSupport.GetString(configuration, "commandType");
        if (!string.IsNullOrWhiteSpace(commandType)
            && string.Equals(commandType, "storedProcedure", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new ValidationError(
                "DB_COMMANDTYPE_DEFERRED",
                "commandType 'storedProcedure' is deferred to 2.4.a.P1 — use 'text' for now~ 💔",
                PropertyName: "commandType"));
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

        // 2️⃣ Read + validate config~ 📋
        var validation = this.ValidateConfiguration(context.Properties);
        if (!validation.IsValid)
        {
            return ModuleResult.Fail(
                $"Invalid configuration: {string.Join("; ", validation.Errors)}~ 💔");
        }

        var query = DbModuleSupport.GetString(context.Properties, "query")!;
        var timeoutSeconds = DbModuleSupport.TryParseInt(context.Properties, "timeoutSeconds") ?? 30;

        DataParameter[] parameters;
        try
        {
            // 🔗 Resolve {{Variable.x}} / {{nodeId.port}} bindings in parameter VALUES, then bind
            // as DataParameters — never concatenated into the SQL (D7)~
            parameters = SqlParameterBinder.Bind(
                SqlParameterTemplateResolver.Resolve(
                    SqlParameterBinder.Normalize(context.Properties.TryGetValue("parameters", out var p) ? p : null),
                    context.Inputs,
                    context.Variables));
        }
        catch (SqlParameterBindingException ex)
        {
            return ModuleResult.Fail(ex.Message, ex);
        }

        var sw = Stopwatch.StartNew();
        DataConnection? db = null;
        var ownsConnection = false;
        try
        {
            // 3️⃣ Reuse an ambient transaction connection when the engine opened one for this connectionId~ 💼
            db = DbModuleSupport.TryGetAmbientConnection(context);
            if (db is null)
            {
                db = await DbModuleSupport
                    .CreateConnectionAsync(factory, context.Properties, cancellationToken)
                    .ConfigureAwait(false);
                ownsConnection = true;
            }

            db.CommandTimeout = timeoutSeconds;

            // 4️⃣ Execute + materialise rows (D8)~ 📊
            var (rows, columns) = ReadAll(db, query, parameters);
            sw.Stop();

            var outputs = new Dictionary<string, object?>
            {
                ["rows"] = rows,
                ["rowCount"] = rows.Count,
                ["columns"] = columns,
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
#pragma warning disable CA1031 // Provider-level SQL errors (Npgsql/Sqlite) are intentionally caught wide and surfaced as a clean Fail~ 🌸
        {
            // Provider-level SQL errors (Npgsql/Sqlite) surface as a clean Fail with context~ 🌸
            sw.Stop();
            return ModuleResult.Fail($"Database query failed: {DbErrorContext.Describe(ex)}~ 💔", ex);
        }
#pragma warning restore CA1031
        finally
        {
            if (ownsConnection && db is not null)
            {
                await db.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Executes the query and projects every row into a column→value dictionary,
    /// capturing the ordered column names once from the reader schema~ 📊.
    /// </summary>
    private static (IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, IReadOnlyList<string> Columns) ReadAll(        DataConnection db,
        string query,
        DataParameter[] parameters)
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        var columns = new List<string>();
        using var reader = db.ExecuteReader(query, parameters);
        IDataReader r = reader.Reader!;

        var fieldCount = r.FieldCount;
        for (var i = 0; i < fieldCount; i++)
        {
            columns.Add(r.GetName(i));
        }

        while (r.Read())
        {
            var row = new Dictionary<string, object?>(fieldCount, StringComparer.Ordinal);
            for (var i = 0; i < fieldCount; i++)
            {
                var value = r.GetValue(i);
                row[columns[i]] = value is DBNull ? null : value;
            }

            rows.Add(row);
        }

        return (rows, columns);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 🌊 Phase 5.1.4 — the streaming source path. This is the whole reason streaming exists: the
    /// <see cref="ReadAll"/> path above materialises every row into a list, so a million-row query
    /// costs a million rows of memory. Here the reader stays open and each row is yielded as it's
    /// read, so memory is bounded by the downstream buffer rather than the result set~ 💾.
    /// </remarks>
    public async IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
        ModuleExecutionContext context,
        IAsyncEnumerable<StreamItem> input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Services.GetService(typeof(IDbConnectionFactory)) is not IDbConnectionFactory factory)
        {
            throw new InvalidOperationException(
                "IDbConnectionFactory not registered in DI. Call services.AddDatabaseModules() at host startup~ 💔");
        }

        var validation = this.ValidateConfiguration(context.Properties);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"Invalid configuration: {string.Join("; ", validation.Errors)}~ 💔");
        }

        var query = DbModuleSupport.GetString(context.Properties, "query")!;
        var timeoutSeconds = DbModuleSupport.TryParseInt(context.Properties, "timeoutSeconds") ?? 30;

        var parameters = SqlParameterBinder.Bind(
            SqlParameterTemplateResolver.Resolve(
                SqlParameterBinder.Normalize(context.Properties.TryGetValue("parameters", out var p) ? p : null),
                context.Inputs,
                context.Variables));

        // An ambient transaction connection belongs to the transaction, so it must not be disposed
        // here — same ownership rule as the batch path~ 💼
        var ambient = DbModuleSupport.TryGetAmbientConnection(context);
        var db = ambient ?? await DbModuleSupport
            .CreateConnectionAsync(factory, context.Properties, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            db.CommandTimeout = timeoutSeconds;

            using var reader = db.ExecuteReader(query, parameters);
            IDataReader r = reader.Reader!;

            var fieldCount = r.FieldCount;
            var columns = new string[fieldCount];
            for (var i = 0; i < fieldCount; i++)
            {
                columns[i] = r.GetName(i);
            }

            long index = 0;
            while (r.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var row = new Dictionary<string, object?>(fieldCount, StringComparer.Ordinal);
                for (var i = 0; i < fieldCount; i++)
                {
                    var value = r.GetValue(i);
                    row[columns[i]] = value is DBNull ? null : value;
                }

                // 📍 The row ordinal doubles as the checkpoint sequence — cheap, and what a future
                // resume would seek with (D12). Nothing consumes it yet.
                yield return StreamItem.FromJson(
                    JsonSerializer.SerializeToElement(row),
                    index,
                    new SourceOffset(index.ToString(CultureInfo.InvariantCulture), index));

                index++;
            }
        }
        finally
        {
            if (ambient is null)
            {
                await db.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
