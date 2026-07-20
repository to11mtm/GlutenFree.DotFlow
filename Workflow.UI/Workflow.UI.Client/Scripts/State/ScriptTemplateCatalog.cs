// <copyright file="ScriptTemplateCatalog.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Scripts.State;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 📄 Phase 3.4.2 (D7) — The static, framework-free catalog of starter templates. Ships ≥10 across
/// languages/categories. Client-side data — not an API (persisted templates are post-MVP 3.4.P2)~ ✨.
/// </summary>
public static class ScriptTemplateCatalog
{
    private static readonly ScriptTemplate[] TemplatesArray = Build();

    /// <summary>Gets every template~ 📚.</summary>
    public static IReadOnlyList<ScriptTemplate> Templates => TemplatesArray;

    /// <summary>Returns the templates for a language (case-insensitive)~ 🌈.</summary>
    /// <param name="language">The language id.</param>
    /// <returns>The matching templates.</returns>
    public static IReadOnlyList<ScriptTemplate> ForLanguage(string? language)
        => string.IsNullOrWhiteSpace(language)
            ? TemplatesArray
            : TemplatesArray.Where(t => string.Equals(t.Language, language, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Groups templates by category (alphabetical), optionally filtered by language~ 🗂️.</summary>
    /// <param name="language">Optional language filter (null = all).</param>
    /// <returns>Category → templates.</returns>
    public static IReadOnlyList<(string Category, IReadOnlyList<ScriptTemplate> Templates)> ByCategory(string? language = null)
    {
        var source = language is null ? (IEnumerable<ScriptTemplate>)TemplatesArray : ForLanguage(language);
        return source
            .GroupBy(t => t.Category)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, (IReadOnlyList<ScriptTemplate>)g.ToList()))
            .ToList();
    }

    private static ScriptTemplate[] Build() => new[]
    {
        new ScriptTemplate(
            "js-http-get", "HTTP GET request", "Fetch JSON from a URL and return it.", "javascript", "HTTP",
            "// Requires network access enabled in the run config.\nconst res = await workflow.httpGet('https://api.example.com/items');\nworkflow.logInfo('status ' + res.status);\nreturn workflow.parseJson(res.body);"),

        new ScriptTemplate(
            "js-http-headers", "HTTP POST with headers", "POST a JSON body with custom headers.", "javascript", "HTTP",
            "const res = await workflow.httpPost(\n  'https://api.example.com/orders',\n  { id: input.id, total: input.total },\n  { 'Authorization': 'Bearer ' + workflow.getVariable('token') });\nreturn { status: res.status };"),

        new ScriptTemplate(
            "js-transform", "Transform (map / filter)", "Map and filter an input array.", "javascript", "Data",
            "const orders = input.orders || [];\nconst large = orders\n  .filter(o => o.amount > 100)\n  .map(o => ({ id: o.id, amount: o.amount }));\nreturn { large, count: large.length };"),

        new ScriptTemplate(
            "js-json", "Parse & build JSON", "Parse a JSON string then build a new object.", "javascript", "Data",
            "const parsed = workflow.parseJson(input.payload);\nconst out = { id: parsed.id, at: workflow.utcNow() };\nreturn workflow.toJson(out);"),

        new ScriptTemplate(
            "js-csv", "CSV round-trip", "Parse CSV, transform, then emit CSV.", "javascript", "Data",
            "const rows = workflow.parseCsv(input.csv, true);\nrows.forEach(r => r.processed = true);\nreturn workflow.toCsv(rows, true);"),

        new ScriptTemplate(
            "js-variables", "Read & write variables", "Read a variable, update it, stage the write.", "javascript", "Variables",
            "const count = workflow.getVariable('count') || 0;\nworkflow.setVariable('count', count + 1);\nreturn { count: count + 1 };"),

        new ScriptTemplate(
            "js-logging", "Logging at each level", "Emit debug/info/warning/error logs.", "javascript", "Logging",
            "workflow.logDebug('starting');\nworkflow.logInfo('processing ' + (input.id || '?'));\nworkflow.logWarning('careful');\n// workflow.logError('something went wrong');\nreturn { ok: true };"),

        new ScriptTemplate(
            "js-hash", "Hash a value", "Compute a SHA-256 hex digest.", "javascript", "Utilities",
            "const digest = workflow.hash(input.value || '', 'sha256');\nworkflow.logInfo('digest ' + digest);\nreturn { digest };"),

        new ScriptTemplate(
            "js-error", "Try / catch error handling", "Guard risky work and surface a clean error.", "javascript", "Error handling",
            "try {\n  const res = await workflow.httpGet(input.url);\n  return workflow.parseJson(res.body);\n} catch (e) {\n  workflow.logError('fetch failed: ' + e.message);\n  throw new Error('Could not load ' + input.url);\n}"),

        new ScriptTemplate(
            "js-database-node", "Database via node composition", "Scripts have no DB API — return a query for a database node to run.", "javascript", "Data",
            "// DotFlow scripts compose with database *nodes* (no direct DB API).\n// Return the query/params for a downstream database node to execute.\nreturn {\n  query: 'SELECT * FROM orders WHERE customer_id = @id',\n  params: { id: input.customerId }\n};"),

        new ScriptTemplate(
            "js-file", "Read & write a file", "Read a file, append a line, write it back.", "javascript", "Files",
            "// Requires file access + an allowed path in the run config.\nconst path = input.path;\nconst existing = (await workflow.fileExists(path)) ? await workflow.readFile(path) : '';\nawait workflow.writeFile(path, existing + '\\n' + workflow.utcNow());\nreturn { ok: true };"),

        new ScriptTemplate(
            "lua-variables", "Read & write variables (Lua)", "Read a variable and stage an updated write.", "lua", "Variables",
            "local count = workflow:getVariable('count') or 0\nworkflow:setVariable('count', count + 1)\nreturn { count = count + 1 }"),

        new ScriptTemplate(
            "lua-logging", "Logging (Lua)", "Log an info message and return.", "lua", "Logging",
            "workflow:logInfo('processing ' .. tostring(input.id))\nreturn { ok = true }"),

        new ScriptTemplate(
            "csharp-transform", "Transform (C#)", "Filter and project an input collection.", "csharp", "Data",
            "var orders = (IEnumerable<dynamic>)Input[\"orders\"];\nvar large = orders.Where(o => (double)o.amount > 100).ToList();\nWorkflow.LogInfo($\"kept {large.Count}\");\nreturn new { count = large.Count };"),
    };
}
