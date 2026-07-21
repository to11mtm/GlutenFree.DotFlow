// <copyright file="TransformScriptModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Transform.Script.Builtin;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Transform.Internal;
using Workflow.Modules.Transform.Script.Abstractions;
using Workflow.Scripting.Roslyn.Abstractions;
using Workflow.Scripting.Roslyn.Execution;

/// <summary>
/// 🌟 Built-in Transform Script module (<c>builtin.transform.script</c>) — runs a typed C# transform
/// body compiled at publish time, HMAC-cached, and executed in a collectible ALC (D2/D16)~ ✨.
/// </summary>
public sealed class TransformScriptModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.transform.script";

    /// <inheritdoc />
    public string DisplayName => "Transform Script (C#)";

    /// <inheritdoc />
    public string Category => "Transformation";

    /// <inheritdoc />
    public string Description => "Runs a typed C# transform body over rows + inputs (compiled + sandboxed)~ 🌟✨";

    /// <inheritdoc />
    public string Icon => "🌟";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition("data", "Data", typeof(object), "Rows the script operates on~ 📥", false)),
        Outputs: Arr.create(
            new PortDefinition("result", "Result", typeof(object), "The script's return value (materialised)~ 📤", false),
            new PortDefinition("rows", "Rows", typeof(object), "Result as rows when it's a record list~ 📋", false),
            new PortDefinition("rowCount", "Row Count", typeof(int), "Row count when the result is a list~ 🔢", false),
            new PortDefinition("success", "Success", typeof(bool), "Whether the script ran~ ✅", false),
            new PortDefinition("durationMs", "Duration (ms)", typeof(long), "Execution duration~ ⏱️", false)),
        Properties: Arr.create(
            new ModulePropertyDefinition("compiledAssemblyKey", "Compiled Assembly Key", typeof(string), "Blob key from the compile step~ 🔑", true, null, PropertyEditorType.Text),
            new ModulePropertyDefinition("inputs", "Inputs", typeof(object), "Named inputs passed to the script~ 🎛️", false, null, PropertyEditorType.Json),
            new ModulePropertyDefinition("timeoutSeconds", "Timeout (s)", typeof(int), "Execution timeout~ ⏱️", false, 30, PropertyEditorType.Number)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        if (TransformSupport.GetString(configuration, "compiledAssemblyKey") is null)
        {
            return ValidationResult.Failure(new ValidationError("KEY_REQUIRED", "compiledAssemblyKey is required~ 💔", PropertyName: "compiledAssemblyKey"));
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var key = TransformSupport.GetString(context.Properties, "compiledAssemblyKey");
        if (key is null)
        {
            return ModuleResult.Fail("🌟 compiledAssemblyKey is required~ 💔");
        }

        var cache = context.Services.GetService<ICompiledScriptCache>();
        var runner = context.Services.GetService<CollectibleScriptRunner>();
        var compiler = context.Services.GetService<ITransformScriptCompiler>();
        if (cache is null || runner is null || compiler is null)
        {
            return ModuleResult.Fail("🌟 Transform script services are not registered — call AddTransformScriptModules() at host startup~ 💔");
        }

        var timeoutSeconds = TransformSupport.GetInt(context.Properties, "timeoutSeconds") ?? 30;

        if (!TransformDataNormalizer.AsRows(TransformSupport.ReadData(context, "data"), out var rows, out var rowErr))
        {
            return ModuleResult.Fail($"🌟 Invalid data: {rowErr}~ 💔");
        }

        var inputs = TransformDataNormalizer.Normalize(context.Properties.GetValueOrDefault("inputs")) as IReadOnlyDictionary<string, object?>
            ?? new Dictionary<string, object?>();

        var bytes = await cache.TryGetAsync(key, cancellationToken).ConfigureAwait(false);
        if (bytes is null)
        {
            return ModuleResult.Fail("🌟 Compiled script not found or failed verification (recompile the node)~ 💔");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var sw = Stopwatch.StartNew();
        try
        {
            var raw = await runner.RunAsync(
                key,
                bytes,
                compiler.EntryTypeName,
                compiler.EntryMethodName,
                new object?[] { rows, inputs, timeoutCts.Token }).ConfigureAwait(false);

            var materialized = ScriptResultMaterializer.Materialize(raw);
            sw.Stop();

            var outputs = new Dictionary<string, object?>
            {
                ["result"] = materialized,
                ["success"] = true,
                ["durationMs"] = sw.ElapsedMilliseconds,
            };

            // Parity with the other transform modules: surface record lists as rows~ 📋
            if (materialized is IReadOnlyList<object?> list && list.All(x => x is IReadOnlyDictionary<string, object?>))
            {
                outputs["rows"] = list;
                outputs["rowCount"] = list.Count;
            }

            return ModuleResult.Ok(outputs, ExecutionMetrics.FromDuration(sw.Elapsed));
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return ModuleResult.Fail($"🌟 Script timed out after {timeoutSeconds}s~ 💔");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var inner = ex.InnerException ?? ex;
            return ModuleResult.Fail($"🌟 Script execution failed: {inner.Message}~ 💔", inner);
        }
    }
}
