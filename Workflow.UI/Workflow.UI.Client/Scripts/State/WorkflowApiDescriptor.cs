// <copyright file="WorkflowApiDescriptor.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Scripts.State;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 💡 Phase 3.4.1 (D4) — The single, hand-authored catalog of the <c>workflow.*</c> surface. Drives
/// Monaco completions/hover **and** the API reference panel. Kept honest against
/// <c>IWorkflowScriptApi</c> by the <c>Descriptor_CoversWorkflowApi_NoDrift</c> guard test. The JS
/// (camelCase) names mirror the prelude in <c>JavaScriptScriptExecutor</c>. Framework-free (D2)~ ✨.
/// </summary>
public static class WorkflowApiDescriptor
{
    /// <summary>
    /// CLR methods on <c>IWorkflowScriptApi</c> that are executor-internal result accessors, not part
    /// of the user-facing <c>workflow.*</c> surface (they have no JS prelude alias). The drift guard
    /// excludes these~ 🔒.
    /// </summary>
    public static readonly IReadOnlyList<string> ExecutorOnlyMembers = new[] { "GetVariableUpdates", "GetLogs" };

    private static readonly ApiMethodInfo[] MethodsArray = BuildMethods();

    /// <summary>Gets every described method (stable order: category then name)~ 📚.</summary>
    public static IReadOnlyList<ApiMethodInfo> Methods => MethodsArray;

    /// <summary>Gets the category names in display order~ 🗂️.</summary>
    public static IReadOnlyList<string> Categories { get; } =
        new[] { "Variables", "Logging", "Utilities", "Context", "HTTP", "Files" };

    /// <summary>Groups the methods by category (in <see cref="Categories"/> order)~ 🗂️.</summary>
    /// <returns>Category → methods.</returns>
    public static IReadOnlyList<(string Category, IReadOnlyList<ApiMethodInfo> Methods)> ByCategory()
        => Categories
            .Select(c => (c, (IReadOnlyList<ApiMethodInfo>)MethodsArray.Where(m => m.Category == c).ToList()))
            .Where(g => g.Item2.Count > 0)
            .ToList();

    /// <summary>Filters methods by a case-insensitive substring over name/summary/category~ 🔍.</summary>
    /// <param name="query">The search text (empty returns all).</param>
    /// <returns>The matching methods.</returns>
    public static IReadOnlyList<ApiMethodInfo> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return MethodsArray;
        }

        var q = query.Trim();
        return MethodsArray
            .Where(m => m.JsName.Contains(q, StringComparison.OrdinalIgnoreCase)
                     || m.Summary.Contains(q, StringComparison.OrdinalIgnoreCase)
                     || m.Category.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static ApiMethodInfo[] BuildMethods()
    {
        var m = new List<ApiMethodInfo>();

        void Add(string clr, string js, string category, string ret, string summary, bool gated, params ApiParam[] ps)
            => m.Add(new ApiMethodInfo(clr, js, category, ret, summary, gated, ps));

        var str = new Func<string, ApiParam>(n => new ApiParam(n, "string"));

        // ── Variables ──
        Add("GetVariable", "getVariable", "Variables", "object?", "Gets a workflow variable value.", false, str("name"));
        Add("SetVariable", "setVariable", "Variables", "void", "Stages a workflow variable write.", false, str("name"), new ApiParam("value", "any"));
        Add("DeleteVariable", "deleteVariable", "Variables", "void", "Stages a variable deletion.", false, str("name"));
        Add("VariableExists", "variableExists", "Variables", "bool", "Checks whether a variable exists.", false, str("name"));

        // ── Logging ──
        Add("LogDebug", "logDebug", "Logging", "void", "Logs a debug message.", false, str("message"));
        Add("LogInfo", "logInfo", "Logging", "void", "Logs an info message.", false, str("message"));
        Add("LogWarning", "logWarning", "Logging", "void", "Logs a warning message.", false, str("message"));
        Add("LogError", "logError", "Logging", "void", "Logs an error message.", false, str("message"));

        // ── Utilities ──
        Add("NewGuid", "newGuid", "Utilities", "string", "Generates a new GUID string.", false);
        Add("Now", "now", "Utilities", "string", "Gets the current local time (ISO 8601).", false);
        Add("UtcNow", "utcNow", "Utilities", "string", "Gets the current UTC time (ISO 8601).", false);
        Add("FormatDateTime", "formatDateTime", "Utilities", "string", "Formats an ISO date with a .NET format.", false, str("isoDate"), str("format"));
        Add("Base64Encode", "base64Encode", "Utilities", "string", "Base64-encodes a UTF-8 string.", false, str("data"));
        Add("Base64Decode", "base64Decode", "Utilities", "string", "Base64-decodes to a UTF-8 string.", false, str("data"));
        Add("Hash", "hash", "Utilities", "string", "Hashes a string (sha256/sha512/md5) → hex.", false, str("data"), str("algorithm"));
        Add("ParseJson", "parseJson", "Utilities", "object?", "Parses a JSON string to a value.", false, str("json"));
        Add("ToJson", "toJson", "Utilities", "string", "Serializes a value to JSON.", false, new ApiParam("value", "any"));
        Add("ParseCsv", "parseCsv", "Utilities", "object", "Parses CSV text into rows.", false, str("csv"), new ApiParam("hasHeader", "bool"));
        Add("ToCsv", "toCsv", "Utilities", "string", "Generates CSV text from rows.", false, new ApiParam("rows", "any"), new ApiParam("includeHeader", "bool"));

        // ── Context ──
        Add("GetExecutionId", "getExecutionId", "Context", "string", "Gets the current execution id.", false);
        Add("GetWorkflowId", "getWorkflowId", "Context", "string", "Gets the current workflow id.", false);
        Add("GetNodeId", "getNodeId", "Context", "string", "Gets the current node id.", false);
        Add("WaitAsync", "wait", "Context", "Promise", "Pauses for a number of milliseconds.", false, new ApiParam("milliseconds", "int"));

        // ── HTTP (gated) ──
        Add("HttpGetAsync", "httpGet", "HTTP", "Promise", "Performs an HTTP GET (requires network).", true, str("url"), new ApiParam("headers?", "object"));
        Add("HttpPostAsync", "httpPost", "HTTP", "Promise", "Performs an HTTP POST (requires network).", true, str("url"), new ApiParam("body", "any"), new ApiParam("headers?", "object"));
        Add("HttpPutAsync", "httpPut", "HTTP", "Promise", "Performs an HTTP PUT (requires network).", true, str("url"), new ApiParam("body", "any"), new ApiParam("headers?", "object"));
        Add("HttpDeleteAsync", "httpDelete", "HTTP", "Promise", "Performs an HTTP DELETE (requires network).", true, str("url"), new ApiParam("headers?", "object"));

        // ── Files (gated) ──
        Add("ReadFileAsync", "readFile", "Files", "Promise", "Reads a file's text (requires file access).", true, str("path"));
        Add("WriteFileAsync", "writeFile", "Files", "Promise", "Writes text to a file (requires file access).", true, str("path"), str("content"));
        Add("FileExists", "fileExists", "Files", "bool", "Checks whether a file exists (requires file access).", true, str("path"));
        Add("DeleteFile", "deleteFile", "Files", "void", "Deletes a file (requires file access).", true, str("path"));

        return m.ToArray();
    }
}
