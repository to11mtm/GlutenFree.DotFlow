// <copyright file="JavaScriptScriptExecutor.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Executors;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Scripting.Abstractions;

/// <summary>
/// 🟨 Phase 3.1 — JavaScript script executor built on Jint (ES2020), generalized from the Phase 2.2.5
/// expression evaluator's safety posture but with script-level limits + the <c>workflow</c> API
/// object + <c>input</c> global injected (D2)~ ✨.
/// </summary>
public sealed class JavaScriptScriptExecutor : IScriptExecutor
{
    private const int RecursionLimit = 128;

    private readonly ILogger<JavaScriptScriptExecutor> logger;

    /// <summary>Initializes a new instance of the <see cref="JavaScriptScriptExecutor"/> class~ 🟨.</summary>
    /// <param name="logger">Optional logger.</param>
    public JavaScriptScriptExecutor(ILogger<JavaScriptScriptExecutor>? logger = null)
    {
        this.logger = logger ?? NullLogger<JavaScriptScriptExecutor>.Instance;
    }

    /// <inheritdoc/>
    public string LanguageId => "javascript";

    /// <inheritdoc/>
    public string DisplayName => "JavaScript";

    /// <inheritdoc/>
    public async Task<ScriptExecutionResult> ExecuteAsync(
        string code,
        ScriptExecutionContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(context);

        var sw = Stopwatch.StartNew();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(context.Config.TimeoutSeconds));
        var linked = timeoutCts.Token;

        try
        {
            var returnValue = await Task.Run(() => this.Run(code, context, linked), linked).ConfigureAwait(false);
            sw.Stop();
            return ScriptExecutionResult.Ok(returnValue, context.Api.GetVariableUpdates(), context.Api.GetLogs(), sw.Elapsed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Timeout fired. Jint surfaces this as either System.TimeoutException (TimeoutInterval)
            // or Jint.Runtime.ExecutionCanceledException (CancellationToken) — neither derives from
            // OperationCanceledException — so catch broadly to report a deterministic timeout~ ⏰.
            sw.Stop();
            return ScriptExecutionResult.Fail($"Script timed out after {context.Config.TimeoutSeconds}s.", context.Api.GetLogs(), sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var message = ex is JavaScriptException jsEx ? jsEx.Message : ex.Message;
            this.logger.LogDebug(ex, "🟨 JavaScript execution failed for node {NodeId}~", context.NodeId);
            return ScriptExecutionResult.Fail($"JavaScript error: {message}", context.Api.GetLogs(), sw.Elapsed);
        }
    }

    private object? Run(string code, ScriptExecutionContext context, CancellationToken ct)
    {
        using var engine = new Engine(opts =>
        {
            opts.LimitMemory(context.Config.MaxMemoryBytes);
            opts.TimeoutInterval(TimeSpan.FromSeconds(context.Config.TimeoutSeconds));
            opts.LimitRecursion(RecursionLimit);
            opts.CancellationToken(ct);
            opts.Strict();
            opts.AllowClr();
            opts.CatchClrExceptions();
            opts.ExperimentalFeatures = ExperimentalFeature.TaskInterop;
        });

        // Expose the CLR api object under a hidden name; the prelude wraps it in camelCase `workflow`~
        engine.SetValue("__api", context.Api);
        engine.SetValue("__inputJson", JsonSerializer.Serialize(context.Inputs));

        engine.Execute(Prelude);

        // Inject libraries as pre-registered modules (workflow.require)~
        foreach (var library in context.Libraries)
        {
            engine.SetValue("__libId", library.LibraryId);
            engine.Execute($"__registerLibrary(__libId, (function(){{ {library.Code}\n }}));");
        }

        // Wrap the user body in an async IIFE so `return` + `await` both work~
        var wrapped = $"(async function() {{\n{code}\n}})();";
        var result = engine.Evaluate(wrapped);
        result = result.UnwrapIfPromise();

        return JsValueToClr(result);
    }

    // Prelude builds the `workflow` object with idiomatic camelCase methods delegating to __api~
    private const string Prelude = @"
var input = JSON.parse(__inputJson);
var __libraries = {};
function __registerLibrary(id, factory) { __libraries[id] = factory(); }
var workflow = {
  getVariable: function(n){ return __api.GetVariable(n); },
  setVariable: function(n,v){ __api.SetVariable(n, v); },
  deleteVariable: function(n){ __api.DeleteVariable(n); },
  variableExists: function(n){ return __api.VariableExists(n); },
  logDebug: function(m){ __api.LogDebug(String(m)); },
  logInfo: function(m){ __api.LogInfo(String(m)); },
  logWarning: function(m){ __api.LogWarning(String(m)); },
  logError: function(m){ __api.LogError(String(m)); },
  log: function(m){ __api.LogInfo(String(m)); },
  newGuid: function(){ return __api.NewGuid(); },
  now: function(){ return __api.Now(); },
  utcNow: function(){ return __api.UtcNow(); },
  formatDateTime: function(d,f){ return __api.FormatDateTime(d, f); },
  base64Encode: function(d){ return __api.Base64Encode(d); },
  base64Decode: function(d){ return __api.Base64Decode(d); },
  hash: function(d,a){ return __api.Hash(d, a); },
  parseJson: function(s){ return JSON.parse(s); },
  toJson: function(o){ return JSON.stringify(o); },
  parseCsv: function(s,h){ return JSON.parse(__api.ToJson(__api.ParseCsv(s, !!h))); },
  toCsv: function(r,h){ return __api.ToCsv(r, h !== false); },
  getExecutionId: function(){ return __api.GetExecutionId(); },
  getWorkflowId: function(){ return __api.GetWorkflowId(); },
  getNodeId: function(){ return __api.GetNodeId(); },
  wait: function(ms){ return __api.WaitAsync(ms); },
  httpGet: function(u,h){ return __api.HttpGetAsync(u, h || null); },
  httpPost: function(u,b,h){ return __api.HttpPostAsync(u, b, h || null); },
  httpPut: function(u,b,h){ return __api.HttpPutAsync(u, b, h || null); },
  httpDelete: function(u,h){ return __api.HttpDeleteAsync(u, h || null); },
  readFile: function(p){ return __api.ReadFileAsync(p); },
  writeFile: function(p,c){ return __api.WriteFileAsync(p, c); },
  fileExists: function(p){ return __api.FileExists(p); },
  deleteFile: function(p){ __api.DeleteFile(p); },
  require: function(id){ if(!(id in __libraries)) throw new Error('Unknown library: ' + id); return __libraries[id]; }
};
";

    private static object? JsValueToClr(JsValue value)
    {
        if (value.IsNull() || value.IsUndefined())
        {
            return null;
        }

        if (value.IsBoolean())
        {
            return value.AsBoolean();
        }

        if (value.IsNumber())
        {
            var d = value.AsNumber();
            if (d == Math.Floor(d) && d >= long.MinValue && d <= long.MaxValue)
            {
                var l = (long)d;
                return l is >= int.MinValue and <= int.MaxValue ? (int)l : l;
            }

            return d;
        }

        if (value.IsString())
        {
            return value.AsString();
        }

        if (value.IsArray())
        {
            var arr = value.AsArray();
            var list = new List<object?>((int)arr.Length);
            for (var i = 0; i < (int)arr.Length; i++)
            {
                list.Add(JsValueToClr(arr[(uint)i]));
            }

            return list;
        }

        if (value.IsObject())
        {
            var obj = value.AsObject();
            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var prop in obj.GetOwnProperties())
            {
                dict[prop.Key.AsString()] = JsValueToClr(prop.Value.Value!);
            }

            return dict;
        }

        return value.ToString();
    }
}
