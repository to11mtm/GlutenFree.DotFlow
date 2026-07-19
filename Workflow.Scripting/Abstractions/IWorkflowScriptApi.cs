// <copyright file="IWorkflowScriptApi.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Abstractions;

using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// 🔧 Phase 3.1 — The <c>workflow</c> object exposed to scripts. Variables/logging/utilities/context
/// are always available; HTTP + file access are capability-gated by <see cref="ScriptExecutionConfig"/>
/// (D6). There is deliberately **no database API** — scripts compose with database *nodes* (Q2)~ ✨.
/// </summary>
/// <remarks>
/// Methods are named PascalCase for CLR callers; script engines expose idiomatic aliases (JS camelCase,
/// Lua snake/camel) via the executor bridge~ 🌸.
/// </remarks>
public interface IWorkflowScriptApi
{
    // ── Variables (staged writes, D7) ───────────────────────────────────────────────

    /// <summary>Gets a workflow variable value (from the read snapshot)~ 💾.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The value, or <c>null</c> when absent.</returns>
    object? GetVariable(string name);

    /// <summary>Stages a workflow variable write (applied by the engine after the node)~ 💾.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value to set.</param>
    void SetVariable(string name, object? value);

    /// <summary>Stages a variable deletion (tombstone applied after the node)~ 🗑️.</summary>
    /// <param name="name">The variable name.</param>
    void DeleteVariable(string name);

    /// <summary>Checks whether a variable exists in the read snapshot~ ❓.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns><c>true</c> when present.</returns>
    bool VariableExists(string name);

    // ── Logging (captured + forwarded) ──────────────────────────────────────────────

    /// <summary>Logs a debug message~ 🐛.</summary>
    /// <param name="message">The message.</param>
    void LogDebug(string message);

    /// <summary>Logs an info message~ ℹ️.</summary>
    /// <param name="message">The message.</param>
    void LogInfo(string message);

    /// <summary>Logs a warning message~ ⚠️.</summary>
    /// <param name="message">The message.</param>
    void LogWarning(string message);

    /// <summary>Logs an error message~ ❌.</summary>
    /// <param name="message">The message.</param>
    void LogError(string message);

    // ── Utilities ───────────────────────────────────────────────────────────────────

    /// <summary>Generates a new GUID string~ 🆔.</summary>
    /// <returns>A new GUID.</returns>
    string NewGuid();

    /// <summary>Gets the current local time (ISO 8601 string)~ 🕒.</summary>
    /// <returns>The current local time.</returns>
    string Now();

    /// <summary>Gets the current UTC time (ISO 8601 string)~ 🕒.</summary>
    /// <returns>The current UTC time.</returns>
    string UtcNow();

    /// <summary>Formats an ISO date string with a .NET format~ 📅.</summary>
    /// <param name="isoDate">The ISO date string.</param>
    /// <param name="format">The .NET format string.</param>
    /// <returns>The formatted date.</returns>
    string FormatDateTime(string isoDate, string format);

    /// <summary>Base64-encodes a UTF-8 string~ 🔤.</summary>
    /// <param name="data">The plaintext.</param>
    /// <returns>Base64.</returns>
    string Base64Encode(string data);

    /// <summary>Base64-decodes to a UTF-8 string~ 🔤.</summary>
    /// <param name="data">The base64 text.</param>
    /// <returns>The decoded plaintext.</returns>
    string Base64Decode(string data);

    /// <summary>Hashes a string (algorithm: <c>sha256</c>/<c>sha512</c>/<c>md5</c>) → hex~ 🔐.</summary>
    /// <param name="data">The input.</param>
    /// <param name="algorithm">The hash algorithm.</param>
    /// <returns>The lowercase hex digest.</returns>
    string Hash(string data, string algorithm);

    /// <summary>Parses a JSON string to a CLR value~ 📦.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The parsed value.</returns>
    object? ParseJson(string json);

    /// <summary>Serializes a CLR value to a JSON string~ 📦.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The JSON text.</returns>
    string ToJson(object? value);

    /// <summary>Parses CSV text into a list of row dictionaries~ 📊.</summary>
    /// <param name="csv">The CSV text.</param>
    /// <param name="hasHeader">Whether the first line is a header.</param>
    /// <returns>The parsed rows.</returns>
    object ParseCsv(string csv, bool hasHeader);

    /// <summary>Generates CSV text from a list of row dictionaries~ 📊.</summary>
    /// <param name="rows">The rows.</param>
    /// <param name="includeHeader">Whether to emit a header line.</param>
    /// <returns>The CSV text.</returns>
    string ToCsv(object? rows, bool includeHeader);

    // ── Workflow context ────────────────────────────────────────────────────────────

    /// <summary>Gets the current execution id~ 🆔.</summary>
    /// <returns>The execution id.</returns>
    string GetExecutionId();

    /// <summary>Gets the current workflow id~ 🆔.</summary>
    /// <returns>The workflow id.</returns>
    string GetWorkflowId();

    /// <summary>Gets the current node id~ 🆔.</summary>
    /// <returns>The node id.</returns>
    string GetNodeId();

    /// <summary>Pauses for a number of milliseconds (capped by remaining timeout)~ ⏸️.</summary>
    /// <param name="milliseconds">How long to wait.</param>
    /// <returns>A task that completes after the delay.</returns>
    Task WaitAsync(int milliseconds);

    // ── Gated HTTP (D6, throws when AllowNetwork is false) ──────────────────────────

    /// <summary>Performs an HTTP GET~ 🌐.</summary>
    /// <param name="url">The URL.</param>
    /// <param name="headers">Optional headers.</param>
    /// <returns>A response object (<c>{ status, headers, body }</c>).</returns>
    Task<object?> HttpGetAsync(string url, object? headers = null);

    /// <summary>Performs an HTTP POST~ 🌐.</summary>
    /// <param name="url">The URL.</param>
    /// <param name="body">The request body.</param>
    /// <param name="headers">Optional headers.</param>
    /// <returns>A response object.</returns>
    Task<object?> HttpPostAsync(string url, object? body, object? headers = null);

    /// <summary>Performs an HTTP PUT~ 🌐.</summary>
    /// <param name="url">The URL.</param>
    /// <param name="body">The request body.</param>
    /// <param name="headers">Optional headers.</param>
    /// <returns>A response object.</returns>
    Task<object?> HttpPutAsync(string url, object? body, object? headers = null);

    /// <summary>Performs an HTTP DELETE~ 🌐.</summary>
    /// <param name="url">The URL.</param>
    /// <param name="headers">Optional headers.</param>
    /// <returns>A response object.</returns>
    Task<object?> HttpDeleteAsync(string url, object? headers = null);

    // ── Gated file system (D6, throws when AllowFileSystem is false) ────────────────

    /// <summary>Reads a file's text (must be under an allowed path)~ 📁.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The file text.</returns>
    Task<string> ReadFileAsync(string path);

    /// <summary>Writes text to a file (must be under an allowed path)~ 📁.</summary>
    /// <param name="path">The file path.</param>
    /// <param name="content">The content.</param>
    /// <returns>A task that completes when written.</returns>
    Task WriteFileAsync(string path, string content);

    /// <summary>Checks whether a file exists (must be under an allowed path)~ 📁.</summary>
    /// <param name="path">The file path.</param>
    /// <returns><c>true</c> when the file exists.</returns>
    bool FileExists(string path);

    /// <summary>Deletes a file (must be under an allowed path)~ 🗑️.</summary>
    /// <param name="path">The file path.</param>
    void DeleteFile(string path);

    // ── Result access (for executors) ───────────────────────────────────────────────

    /// <summary>Gets the variable writes staged so far~ 💾.</summary>
    /// <returns>The staged variable updates.</returns>
    IReadOnlyDictionary<string, object?> GetVariableUpdates();

    /// <summary>Gets the log entries captured so far~ 📝.</summary>
    /// <returns>The captured logs.</returns>
    IReadOnlyList<ScriptLogEntry> GetLogs();
}
