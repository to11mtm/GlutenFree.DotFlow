// <copyright file="LuaWorkflowApiAdapter.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Lua;

using System;
using Workflow.Scripting.Abstractions;

/// <summary>
/// 🌙 Phase 3.1.3 — A MoonSharp-friendly synchronous facade over <see cref="IWorkflowScriptApi"/>.
/// Lua scripts are synchronous in MVP (Q7), so the async HTTP/file/wait methods are exposed as
/// blocking wrappers with cancellation propagation. Method names stay PascalCase; the Lua prelude
/// maps them to idiomatic camelCase~ ✨.
/// </summary>
public sealed class LuaWorkflowApiAdapter
{
    private readonly IWorkflowScriptApi api;

    /// <summary>Initializes a new instance of the <see cref="LuaWorkflowApiAdapter"/> class~ 🌙.</summary>
    /// <param name="api">The underlying workflow script API.</param>
    public LuaWorkflowApiAdapter(IWorkflowScriptApi api)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
    }

    // Variables + logging + utilities + context are already synchronous~
    public object? GetVariable(string name) => this.api.GetVariable(name);

    public void SetVariable(string name, object? value) => this.api.SetVariable(name, value);

    public void DeleteVariable(string name) => this.api.DeleteVariable(name);

    public bool VariableExists(string name) => this.api.VariableExists(name);

    public void LogDebug(string message) => this.api.LogDebug(message);

    public void LogInfo(string message) => this.api.LogInfo(message);

    public void LogWarning(string message) => this.api.LogWarning(message);

    public void LogError(string message) => this.api.LogError(message);

    public string NewGuid() => this.api.NewGuid();

    public string Now() => this.api.Now();

    public string UtcNow() => this.api.UtcNow();

    public string FormatDateTime(string isoDate, string format) => this.api.FormatDateTime(isoDate, format);

    public string Base64Encode(string data) => this.api.Base64Encode(data);

    public string Base64Decode(string data) => this.api.Base64Decode(data);

    public string Hash(string data, string algorithm) => this.api.Hash(data, algorithm);

    public object? ParseJson(string json) => this.api.ParseJson(json);

    public string ToJson(object? value) => this.api.ToJson(value);

    public object ParseCsv(string csv, bool hasHeader) => this.api.ParseCsv(csv, hasHeader);

    public string ToCsv(object? rows, bool includeHeader) => this.api.ToCsv(rows, includeHeader);

    public string GetExecutionId() => this.api.GetExecutionId();

    public string GetWorkflowId() => this.api.GetWorkflowId();

    public string GetNodeId() => this.api.GetNodeId();

    // Blocking wrappers for the async methods (Lua is synchronous in MVP)~
    public void Wait(int milliseconds) => this.api.WaitAsync(milliseconds).GetAwaiter().GetResult();

    public object? HttpGet(string url, object? headers) => this.api.HttpGetAsync(url, headers).GetAwaiter().GetResult();

    public object? HttpPost(string url, object? body, object? headers) => this.api.HttpPostAsync(url, body, headers).GetAwaiter().GetResult();

    public object? HttpPut(string url, object? body, object? headers) => this.api.HttpPutAsync(url, body, headers).GetAwaiter().GetResult();

    public object? HttpDelete(string url, object? headers) => this.api.HttpDeleteAsync(url, headers).GetAwaiter().GetResult();

    public string ReadFile(string path) => this.api.ReadFileAsync(path).GetAwaiter().GetResult();

    public void WriteFile(string path, string content) => this.api.WriteFileAsync(path, content).GetAwaiter().GetResult();

    public bool FileExists(string path) => this.api.FileExists(path);

    public void DeleteFile(string path) => this.api.DeleteFile(path);
}
