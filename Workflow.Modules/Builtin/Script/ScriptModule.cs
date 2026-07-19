// <copyright file="ScriptModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Script;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Scripting.Abstractions;
using Workflow.Scripting.Api;

/// <summary>
/// 📜 Phase 3.1.4 — General-purpose script node (<c>builtin.script</c>). Runs a JavaScript / Lua / C#
/// script in the sandbox, flowing <c>input</c> in and the return value out, with staged variable
/// writes applied by the engine (D7/D8). Executors are resolved from DI via
/// <see cref="IScriptExecutorFactory"/>~ ✨.
/// </summary>
public sealed class ScriptModule : IWorkflowModule
{
    /// <inheritdoc/>
    public string ModuleId => "builtin.script";

    /// <inheritdoc/>
    public string DisplayName => "Script";

    /// <inheritdoc/>
    public string Category => "Scripting";

    /// <inheritdoc/>
    public string Description => "Runs a JavaScript, Lua, or C# script in a sandbox~ 📜✨";

    /// <inheritdoc/>
    public string Icon => "📜";

    /// <inheritdoc/>
    public Version Version => new(1, 0, 0);

    /// <inheritdoc/>
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition("input", "Input", typeof(object), "Data passed to the script as `input`~ 📥", false)),
        Outputs: Arr.create(
            new PortDefinition("result", "Result", typeof(object), "The script's return value~ 📤", false),
            new PortDefinition("success", "Success", typeof(bool), "Whether the script ran~ ✅", false),
            new PortDefinition("durationMs", "Duration (ms)", typeof(long), "Execution duration~ ⏱️", false)),
        Properties: Arr.create(
            new ModulePropertyDefinition("language", "Language", typeof(string), "Script language~ 🈂️", true, "javascript", PropertyEditorType.Dropdown, Arr.create<object>("javascript", "lua", "csharp")),
            new ModulePropertyDefinition("code", "Code", typeof(string), "The script body~ 💻", true, null, PropertyEditorType.Code),
            new ModulePropertyDefinition("timeoutSeconds", "Timeout (s)", typeof(int), "Execution timeout~ ⏱️", false, 30, PropertyEditorType.Number),
            new ModulePropertyDefinition("allowNetwork", "Allow Network", typeof(bool), "Permit HTTP calls~ 🌐", false, false, PropertyEditorType.Boolean),
            new ModulePropertyDefinition("allowFileSystem", "Allow File System", typeof(bool), "Permit file access~ 📁", false, false, PropertyEditorType.Boolean),
            new ModulePropertyDefinition("allowedPaths", "Allowed Paths", typeof(object), "Paths the script may touch~ 📂", false, null, PropertyEditorType.Json)));

    /// <inheritdoc/>
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        var language = GetString(configuration, "language");
        if (string.IsNullOrWhiteSpace(language))
        {
            return ValidationResult.Failure(new ValidationError("SCRIPT_LANG", "A script language is required~ 💔", PropertyName: "language"));
        }

        if (string.IsNullOrWhiteSpace(GetString(configuration, "code")))
        {
            return ValidationResult.Failure(new ValidationError("SCRIPT_CODE", "Script code is required~ 💔", PropertyName: "code"));
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc/>
    public async Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var factory = context.Services.GetService<IScriptExecutorFactory>();
        if (factory is null)
        {
            return ModuleResult.Fail("📜 Scripting is not configured — call AddWorkflowScripting() at host startup~ 💔");
        }

        var language = GetString(context.Properties, "language") ?? "javascript";
        var executor = factory.GetExecutor(language);
        if (executor is null)
        {
            var available = string.Join(", ", factory.GetRegisteredLanguages().Select(l => l.LanguageId));
            return ModuleResult.Fail($"📜 Unknown script language '{language}'. Registered: {available}~ 💔");
        }

        var code = GetString(context.Properties, "code");
        if (string.IsNullOrWhiteSpace(code))
        {
            return ModuleResult.Fail("📜 Script code is required~ 💔");
        }

        // Build config from node properties, clamped to host ceilings~
        var ceilings = context.Services.GetService<ScriptHostCeilings>() ?? ScriptHostCeilings.Default;
        var requested = new ScriptExecutionConfig
        {
            TimeoutSeconds = GetInt(context.Properties, "timeoutSeconds") ?? 30,
            AllowNetwork = GetBool(context.Properties, "allowNetwork") ?? false,
            AllowFileSystem = GetBool(context.Properties, "allowFileSystem") ?? false,
            AllowedPaths = GetStringList(context.Properties, "allowedPaths"),
        };
        var config = requested.ClampTo(ceilings);

        var inputs = context.Inputs.TryGetValue("input", out var inputValue) && inputValue is IReadOnlyDictionary<string, object?> inputDict
            ? inputDict
            : context.Inputs;

        var api = new WorkflowScriptApi(new WorkflowScriptApiOptions
        {
            Variables = context.Variables,
            Config = config,
            ExecutionId = context.ExecutionId,
            NodeId = context.NodeId,
            Logger = context.Logger,
            HttpClientFactory = context.Services.GetService<IHttpClientFactory>(),
            CancellationToken = cancellationToken,
        });

        var scriptContext = new ScriptExecutionContext
        {
            Inputs = inputs,
            Variables = context.Variables,
            Api = api,
            Config = config,
            ExecutionId = context.ExecutionId,
            NodeId = context.NodeId,
            Logger = context.Logger,
        };

        var result = await executor.ExecuteAsync(code, scriptContext, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return ModuleResult.Fail($"📜 {result.Error}~ 💔");
        }

        var outputs = new Dictionary<string, object?>
        {
            ["result"] = result.ReturnValue,
            ["success"] = true,
            ["durationMs"] = (long)result.Duration.TotalMilliseconds,
        };

        return result.VariableUpdates.Count > 0
            ? ModuleResult.Ok(outputs, new Dictionary<string, object?>(result.VariableUpdates))
            : ModuleResult.Ok(outputs);
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> props, string key)
        => props.TryGetValue(key, out var v) ? v?.ToString() : null;

    private static int? GetInt(IReadOnlyDictionary<string, object?> props, string key)
    {
        if (!props.TryGetValue(key, out var v) || v is null)
        {
            return null;
        }

        return v switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            _ => int.TryParse(v.ToString(), out var parsed) ? parsed : null,
        };
    }

    private static bool? GetBool(IReadOnlyDictionary<string, object?> props, string key)
    {
        if (!props.TryGetValue(key, out var v) || v is null)
        {
            return null;
        }

        return v switch
        {
            bool b => b,
            _ => bool.TryParse(v.ToString(), out var parsed) ? parsed : null,
        };
    }

    private static IReadOnlyList<string> GetStringList(IReadOnlyDictionary<string, object?> props, string key)
    {
        if (!props.TryGetValue(key, out var v) || v is null)
        {
            return Array.Empty<string>();
        }

        if (v is IEnumerable<object?> list)
        {
            return list.Where(x => x is not null).Select(x => x!.ToString()!).ToList();
        }

        if (v is string s && !string.IsNullOrWhiteSpace(s))
        {
            return new[] { s };
        }

        return Array.Empty<string>();
    }
}
