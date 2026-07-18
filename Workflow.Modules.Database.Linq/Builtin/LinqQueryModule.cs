// <copyright file="LinqQueryModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq.Builtin;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Linq.Abstractions;
using Workflow.Modules.Database.Linq.Execution;

/// <summary>
/// 🌟 The primary typed authoring surface (<c>builtin.database.linq</c>) — Phase 2.4.b.3 (D12)~ ✨💖.
/// Loads the publish-time-compiled assembly from the cache, runs it in a collectible ALC against a
/// named connection, and returns fully-materialised rows (no ALC-rooted references escape — D8)~ 🌸.
/// </summary>
/// <remarks>
/// CopilotNote: Parameterless-constructable for reflective discovery. Resolves the compiler cache,
/// ALC runner, and connection factory from <see cref="ModuleExecutionContext.Services"/> — the host
/// wires the family via <c>services.AddDatabaseLinqModules()</c> (D14, opt-in so Roslyn stays
/// quarantined). User code never sees raw connection strings (mitigates C3)~ 🧠.
/// </remarks>
public sealed class LinqQueryModule : IWorkflowModule
{
    /// <inheritdoc/>
    public string ModuleId => "builtin.database.linq";

    /// <inheritdoc/>
    public string DisplayName => "Database Linq (typed)";

    /// <inheritdoc/>
    public string Category => "Database";

    /// <inheritdoc/>
    public string Description => "Runs a typed, Roslyn-compiled linq2db query against a selected table catalog~ 🌟✨";

    /// <inheritdoc/>
    public string Icon => "🌟";

    /// <inheritdoc/>
    public Version Version => new(1, 0, 0);

    /// <inheritdoc/>
    public ModuleSchema Schema => new(
        Inputs: Arr<PortDefinition>.Empty,
        Outputs: Arr.create(
            new PortDefinition("rows", "Rows", typeof(IReadOnlyList<IReadOnlyDictionary<string, object?>>), "Materialised result rows~ 📊", false),
            new PortDefinition("rowCount", "Row Count", typeof(int), "Number of rows returned~ 🔢", false),
            new PortDefinition("result", "Result", typeof(object), "The raw materialised result (rows or scalar)~ 📤", false),
            new PortDefinition("success", "Success", typeof(bool), "True when the query executed without error~ ✅", false),
            new PortDefinition("durationMs", "Duration (ms)", typeof(long), "Round-trip elapsed time~ ⏱️", false)),
        Properties: Arr.create(
            new ModulePropertyDefinition(
                "connectionId",
                "Connection Id",
                typeof(string),
                "Named connection id (required — the typed path never takes a raw connection string, C3)~ 📇",
                IsRequired: true,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text),
            new ModulePropertyDefinition(
                "compiledAssemblyKey",
                "Compiled Assembly Key",
                typeof(string),
                "Blob key of the publish-time-compiled assembly (from POST /api/database/linq/compile)~ 🔑",
                IsRequired: true,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text),
            new ModulePropertyDefinition(
                "inputs",
                "Inputs",
                typeof(Dictionary<string, object?>),
                "Named input values, exposed to user code via the typed LinqInputs struct~ 🧬",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Json),
            new ModulePropertyDefinition(
                "timeoutSeconds",
                "Timeout (seconds)",
                typeof(int),
                "Command timeout in seconds (default 30)~ ⏱️",
                IsRequired: false,
                DefaultValue: 30,
                EditorType: PropertyEditorType.Number)));

    /// <inheritdoc/>
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(GetString(configuration, "connectionId")))
        {
            errors.Add(new ValidationError("LINQ_CONNECTION_REQUIRED", "'connectionId' is required~ 💔", PropertyName: "connectionId"));
        }

        if (string.IsNullOrWhiteSpace(GetString(configuration, "compiledAssemblyKey")))
        {
            errors.Add(new ValidationError("LINQ_KEY_REQUIRED", "'compiledAssemblyKey' is required (compile at publish first)~ 💔", PropertyName: "compiledAssemblyKey"));
        }

        return errors.Count > 0 ? ValidationResult.Failure(errors.ToArray()) : ValidationResult.Success();
    }

    /// <inheritdoc/>
    public async Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Services.GetService(typeof(ICompiledAssemblyCache)) is not ICompiledAssemblyCache cache)
        {
            return ModuleResult.Fail("ICompiledAssemblyCache not registered. Call services.AddDatabaseLinqModules() at host startup~ 💔");
        }

        if (context.Services.GetService(typeof(ILinqScriptRunner)) is not ILinqScriptRunner runner)
        {
            return ModuleResult.Fail("ILinqScriptRunner not registered. Call services.AddDatabaseLinqModules() at host startup~ 💔");
        }

        if (context.Services.GetService(typeof(IDbConnectionFactory)) is not IDbConnectionFactory factory)
        {
            return ModuleResult.Fail("IDbConnectionFactory not registered. Call services.AddDatabaseModules() at host startup~ 💔");
        }

        var validation = this.ValidateConfiguration(context.Properties);
        if (!validation.IsValid)
        {
            return ModuleResult.Fail($"Invalid configuration: {string.Join("; ", validation.Errors)}~ 💔");
        }

        var connectionId = GetString(context.Properties, "connectionId")!;
        var key = GetString(context.Properties, "compiledAssemblyKey")!;
        var timeoutSeconds = TryGetInt(context.Properties, "timeoutSeconds") ?? 30;
        var inputs = NormalizeInputs(context.Properties.TryGetValue("inputs", out var rawInputs) ? rawInputs : null);

        var sw = Stopwatch.StartNew();
        try
        {
            var bytes = await cache.TryGetAsync(key, cancellationToken).ConfigureAwait(false);
            if (bytes is null)
            {
                sw.Stop();
                return ModuleResult.Fail(
                    $"No compiled assembly found for key '{key}' (not compiled yet, evicted, or failed HMAC verification)~ 💔");
            }

            var options = await factory.CreateOptionsAsync(connectionId, cancellationToken).ConfigureAwait(false);
            var run = await runner.RunAsync(key, bytes, options, inputs, timeoutSeconds, cancellationToken).ConfigureAwait(false);
            sw.Stop();

            var outputs = new Dictionary<string, object?>
            {
                ["rows"] = run.Rows ?? (IReadOnlyList<IReadOnlyDictionary<string, object?>>)Array.Empty<IReadOnlyDictionary<string, object?>>(),
                ["rowCount"] = run.RowCount ?? 0,
                ["result"] = run.Result,
                ["success"] = true,
                ["durationMs"] = sw.ElapsedMilliseconds,
            };

            return ModuleResult.Ok(outputs, ExecutionMetrics.FromDuration(sw.Elapsed));
        }
        catch (LinqMaterializationException ex)
        {
            sw.Stop();
            return ModuleResult.Fail(ex.Message, ex);
        }
        catch (OperationCanceledException ex)
        {
            sw.Stop();
            return ModuleResult.Fail("Linq execution was cancelled~ 💔", ex);
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
#pragma warning disable CA1031 // Provider/user-code errors are intentionally caught wide and surfaced as a clean Fail~ 🌸
        {
            sw.Stop();
            return ModuleResult.Fail($"Linq execution failed: {ex.Message}~ 💔", ex);
        }
#pragma warning restore CA1031
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> config, string key)
        => config.TryGetValue(key, out var v) && v is not null ? v.ToString() : null;

    private static int? TryGetInt(IReadOnlyDictionary<string, object?> config, string key)
    {
        if (!config.TryGetValue(key, out var v) || v is null)
        {
            return null;
        }

        return v switch
        {
            int i => i,
            long l => (int)l,
            _ => int.TryParse(v.ToString(), out var parsed) ? parsed : null,
        };
    }

    private static IReadOnlyDictionary<string, object?> NormalizeInputs(object? raw)
    {
        switch (raw)
        {
            case null:
                return new Dictionary<string, object?>();
            case IReadOnlyDictionary<string, object?> ro:
                return ro;
            case IDictionary<string, object?> d:
                return new Dictionary<string, object?>(d);
            case System.Collections.IDictionary legacy:
            {
                var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (System.Collections.DictionaryEntry entry in legacy)
                {
                    dict[entry.Key?.ToString() ?? string.Empty] = entry.Value;
                }

                return dict;
            }

            default:
                return new Dictionary<string, object?>();
        }
    }
}

