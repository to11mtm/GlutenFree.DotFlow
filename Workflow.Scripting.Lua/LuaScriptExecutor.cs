// <copyright file="LuaScriptExecutor.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Lua;

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MoonSharp.Interpreter;
using Workflow.Scripting.Abstractions;
using ScriptExecutionContext = Workflow.Scripting.Abstractions.ScriptExecutionContext;

/// <summary>
/// 🌙 Phase 3.1.3 — Lua script executor built on MoonSharp (soft-sandboxed, pure-managed). The script
/// body is loaded as a function and run via a coroutine with an auto-yield counter so the executor
/// can interrupt CPU-bound scripts on cancellation/timeout — this shape is also what 3.1.P5 wraps for
/// async coroutine bridging (D4/Q7)~ ✨.
/// </summary>
public sealed class LuaScriptExecutor : IScriptExecutor
{
    private const long AutoYieldInstructions = 20000;

    private readonly ILogger<LuaScriptExecutor> logger;

    static LuaScriptExecutor()
    {
        // Expose the synchronous API facade to Lua as a UserData type~
        UserData.RegisterType<LuaWorkflowApiAdapter>();
    }

    /// <summary>Initializes a new instance of the <see cref="LuaScriptExecutor"/> class~ 🌙.</summary>
    /// <param name="logger">Optional logger.</param>
    public LuaScriptExecutor(ILogger<LuaScriptExecutor>? logger = null)
    {
        this.logger = logger ?? NullLogger<LuaScriptExecutor>.Instance;
    }

    /// <inheritdoc/>
    public string LanguageId => "lua";

    /// <inheritdoc/>
    public string DisplayName => "Lua";

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
        catch (OperationCanceledException) when (linked.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            sw.Stop();
            return ScriptExecutionResult.Fail($"Script timed out after {context.Config.TimeoutSeconds}s.", context.Api.GetLogs(), sw.Elapsed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            var message = ex is ScriptRuntimeException sre ? sre.DecoratedMessage : ex.Message;
            this.logger.LogDebug(ex, "🌙 Lua execution failed for node {NodeId}~", context.NodeId);
            return ScriptExecutionResult.Fail($"Lua error: {message}", context.Api.GetLogs(), sw.Elapsed);
        }
    }

    private object? Run(string code, ScriptExecutionContext context, CancellationToken ct)
    {
        var script = new Script(CoreModules.Preset_SoftSandbox);

        // Inject the api facade + input table + libraries~
        var adapter = new LuaWorkflowApiAdapter(context.Api);
        script.Globals["__api"] = UserData.Create(adapter);
        script.Globals["input"] = LuaMarshaller.ToLua(script, context.Inputs);

        script.DoString(Prelude);

        foreach (var library in context.Libraries)
        {
            var factory = script.LoadString($"return (function() {library.Code}\n end)()");
            var value = script.Call(factory);
            ((Table)script.Globals["__libraries"]).Set(library.LibraryId, value);
        }

        // Load the body as a coroutine so auto-yield lets us honour cancellation between chunks~
        var chunk = script.LoadString("local function __main()\n" + code + "\nend\nreturn __main()");
        var coroutineHandle = script.CreateCoroutine(chunk);
        var coroutine = coroutineHandle.Coroutine;
        coroutine.AutoYieldCounter = AutoYieldInstructions;

        DynValue result;
        do
        {
            ct.ThrowIfCancellationRequested();
            result = coroutine.Resume();
        }
        while (coroutine.State is CoroutineState.Suspended or CoroutineState.ForceSuspended);

        return LuaMarshaller.FromLua(result);
    }

    // Builds the camelCase `workflow` table delegating to the CLR adapter (__api)~
    private const string Prelude = @"
__libraries = {}
workflow = {
  getVariable = function(n) return __api.GetVariable(n) end,
  setVariable = function(n, v) __api.SetVariable(n, v) end,
  deleteVariable = function(n) __api.DeleteVariable(n) end,
  variableExists = function(n) return __api.VariableExists(n) end,
  logDebug = function(m) __api.LogDebug(tostring(m)) end,
  logInfo = function(m) __api.LogInfo(tostring(m)) end,
  logWarning = function(m) __api.LogWarning(tostring(m)) end,
  logError = function(m) __api.LogError(tostring(m)) end,
  log = function(m) __api.LogInfo(tostring(m)) end,
  newGuid = function() return __api.NewGuid() end,
  now = function() return __api.Now() end,
  utcNow = function() return __api.UtcNow() end,
  formatDateTime = function(d, f) return __api.FormatDateTime(d, f) end,
  base64Encode = function(d) return __api.Base64Encode(d) end,
  base64Decode = function(d) return __api.Base64Decode(d) end,
  hash = function(d, a) return __api.Hash(d, a) end,
  parseJson = function(s) return __api.ParseJson(s) end,
  toJson = function(o) return __api.ToJson(o) end,
  parseCsv = function(s, h) return __api.ParseCsv(s, h == true) end,
  toCsv = function(r, h) return __api.ToCsv(r, h ~= false) end,
  getExecutionId = function() return __api.GetExecutionId() end,
  getWorkflowId = function() return __api.GetWorkflowId() end,
  getNodeId = function() return __api.GetNodeId() end,
  wait = function(ms) __api.Wait(ms) end,
  httpGet = function(u, h) return __api.HttpGet(u, h) end,
  httpPost = function(u, b, h) return __api.HttpPost(u, b, h) end,
  httpPut = function(u, b, h) return __api.HttpPut(u, b, h) end,
  httpDelete = function(u, h) return __api.HttpDelete(u, h) end,
  readFile = function(p) return __api.ReadFile(p) end,
  writeFile = function(p, c) __api.WriteFile(p, c) end,
  fileExists = function(p) return __api.FileExists(p) end,
  deleteFile = function(p) __api.DeleteFile(p) end,
  require = function(id)
    local lib = __libraries[id]
    if lib == nil then error('Unknown library: ' .. tostring(id)) end
    return lib
  end
}
";
}
