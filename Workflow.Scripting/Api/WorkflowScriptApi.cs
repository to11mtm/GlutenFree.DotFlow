// <copyright file="WorkflowScriptApi.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Api;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Scripting.Abstractions;

/// <summary>
/// 🔧 Phase 3.1 — Default <see cref="IWorkflowScriptApi"/>: variables (staged), logging (captured +
/// forwarded), utilities, workflow context, and capability-gated HTTP/file access. No database API
/// (Q2)~ ✨.
/// </summary>
public sealed class WorkflowScriptApi : IWorkflowScriptApi
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IReadOnlyDictionary<string, object?> variables;
    private readonly ScriptExecutionConfig config;
    private readonly Guid executionId;
    private readonly Guid workflowId;
    private readonly string nodeId;
    private readonly ILogger logger;
    private readonly IHttpClientFactory? httpClientFactory;
    private readonly CancellationToken cancellationToken;

    private readonly Dictionary<string, object?> variableUpdates = new();
    private readonly List<ScriptLogEntry> logs = new();
    private int httpRequestCount;

    /// <summary>Initializes a new instance of the <see cref="WorkflowScriptApi"/> class~ 🔧.</summary>
    /// <param name="options">The construction options.</param>
    public WorkflowScriptApi(WorkflowScriptApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.variables = options.Variables ?? new Dictionary<string, object?>();
        this.config = options.Config ?? ScriptExecutionConfig.Default;
        this.executionId = options.ExecutionId;
        this.workflowId = options.WorkflowId;
        this.nodeId = options.NodeId ?? string.Empty;
        this.logger = options.Logger ?? NullLogger.Instance;
        this.httpClientFactory = options.HttpClientFactory;
        this.cancellationToken = options.CancellationToken;
    }

    /// <inheritdoc/>
    public object? GetVariable(string name)
    {
        if (this.variableUpdates.TryGetValue(name, out var staged))
        {
            return staged;
        }

        return this.variables.TryGetValue(name, out var value) ? value : null;
    }

    /// <inheritdoc/>
    public void SetVariable(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        this.variableUpdates[name] = value;
    }

    /// <inheritdoc/>
    public void DeleteVariable(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        // ModuleResult.VariableUpdates only supports name→value merges, so a delete stages null~
        this.variableUpdates[name] = null;
    }

    /// <inheritdoc/>
    public bool VariableExists(string name)
        => this.variableUpdates.ContainsKey(name) || this.variables.ContainsKey(name);

    /// <inheritdoc/>
    public void LogDebug(string message) => this.Capture("debug", message);

    /// <inheritdoc/>
    public void LogInfo(string message) => this.Capture("info", message);

    /// <inheritdoc/>
    public void LogWarning(string message) => this.Capture("warning", message);

    /// <inheritdoc/>
    public void LogError(string message) => this.Capture("error", message);

    /// <inheritdoc/>
    public string NewGuid() => Guid.NewGuid().ToString();

    /// <inheritdoc/>
    public string Now() => DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public string UtcNow() => DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public string FormatDateTime(string isoDate, string format)
    {
        var dt = DateTimeOffset.Parse(isoDate, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        return dt.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc/>
    public string Base64Encode(string data) => Convert.ToBase64String(Encoding.UTF8.GetBytes(data ?? string.Empty));

    /// <inheritdoc/>
    public string Base64Decode(string data) => Encoding.UTF8.GetString(Convert.FromBase64String(data ?? string.Empty));

    /// <inheritdoc/>
    public string Hash(string data, string algorithm)
    {
        var bytes = Encoding.UTF8.GetBytes(data ?? string.Empty);
        byte[] hash = (algorithm ?? "sha256").ToLowerInvariant() switch
        {
            "sha256" => SHA256.HashData(bytes),
            "sha512" => SHA512.HashData(bytes),
            "sha1" => SHA1.HashData(bytes),
            "md5" => MD5.HashData(bytes),
            _ => throw new ArgumentException($"Unsupported hash algorithm '{algorithm}'."),
        };

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <inheritdoc/>
    public object? ParseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        return JsonToClr(doc.RootElement);
    }

    /// <inheritdoc/>
    public string ToJson(object? value) => JsonSerializer.Serialize(value, JsonOptions);

    /// <inheritdoc/>
    public object ParseCsv(string csv, bool hasHeader)
    {
        var rows = new List<object?>();
        if (string.IsNullOrEmpty(csv))
        {
            return rows;
        }

        var lines = csv.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return rows;
        }

        string[] headers;
        var start = 0;
        if (hasHeader)
        {
            headers = SplitCsvLine(lines[0]);
            start = 1;
        }
        else
        {
            headers = Enumerable.Range(0, SplitCsvLine(lines[0]).Length).Select(i => $"column{i}").ToArray();
        }

        for (var i = start; i < lines.Length; i++)
        {
            var cells = SplitCsvLine(lines[i]);
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var c = 0; c < headers.Length; c++)
            {
                row[headers[c]] = c < cells.Length ? cells[c] : null;
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <inheritdoc/>
    public string ToCsv(object? rows, bool includeHeader)
    {
        if (rows is not IEnumerable<object?> list)
        {
            return string.Empty;
        }

        var records = list.OfType<IReadOnlyDictionary<string, object?>>().ToList();
        if (records.Count == 0)
        {
            return string.Empty;
        }

        var headers = records[0].Keys.ToList();
        var sb = new StringBuilder();
        if (includeHeader)
        {
            sb.AppendLine(string.Join(",", headers.Select(EscapeCsv)));
        }

        foreach (var record in records)
        {
            sb.AppendLine(string.Join(",", headers.Select(h => EscapeCsv(record.TryGetValue(h, out var v) ? v?.ToString() ?? string.Empty : string.Empty))));
        }

        return sb.ToString().TrimEnd('\r', '\n');
    }

    /// <inheritdoc/>
    public string GetExecutionId() => this.executionId.ToString();

    /// <inheritdoc/>
    public string GetWorkflowId() => this.workflowId.ToString();

    /// <inheritdoc/>
    public string GetNodeId() => this.nodeId;

    /// <inheritdoc/>
    public async Task WaitAsync(int milliseconds)
    {
        var capped = Math.Clamp(milliseconds, 0, this.config.TimeoutSeconds * 1000);
        await Task.Delay(capped, this.cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<object?> HttpGetAsync(string url, object? headers = null) => this.SendAsync(HttpMethod.Get, url, null, headers);

    /// <inheritdoc/>
    public Task<object?> HttpPostAsync(string url, object? body, object? headers = null) => this.SendAsync(HttpMethod.Post, url, body, headers);

    /// <inheritdoc/>
    public Task<object?> HttpPutAsync(string url, object? body, object? headers = null) => this.SendAsync(HttpMethod.Put, url, body, headers);

    /// <inheritdoc/>
    public Task<object?> HttpDeleteAsync(string url, object? headers = null) => this.SendAsync(HttpMethod.Delete, url, null, headers);

    /// <inheritdoc/>
    public async Task<string> ReadFileAsync(string path)
    {
        this.EnsureFileAllowed(path);
        return await File.ReadAllTextAsync(path, this.cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task WriteFileAsync(string path, string content)
    {
        this.EnsureFileAllowed(path);
        await File.WriteAllTextAsync(path, content ?? string.Empty, this.cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public bool FileExists(string path)
    {
        this.EnsureFileAllowed(path);
        return File.Exists(path);
    }

    /// <inheritdoc/>
    public void DeleteFile(string path)
    {
        this.EnsureFileAllowed(path);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, object?> GetVariableUpdates() => this.variableUpdates;

    /// <inheritdoc/>
    public IReadOnlyList<ScriptLogEntry> GetLogs() => this.logs;

    private static object? JsonToClr(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var prop in element.EnumerateObject())
                {
                    obj[prop.Name] = JsonToClr(prop.Value);
                }

                return obj;
            case JsonValueKind.Array:
                return element.EnumerateArray().Select(JsonToClr).ToList();
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                return element.TryGetInt64(out var l) ? l : element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            default:
                return null;
        }
    }

    private static string[] SplitCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(ch);
                }
            }
            else if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == ',')
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(ch);
            }
        }

        result.Add(sb.ToString());
        return result.ToArray();
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    private void Capture(string level, string message)
    {
        var text = message ?? string.Empty;
        this.logs.Add(new ScriptLogEntry(level, text));
        switch (level)
        {
            case "debug":
                this.logger.LogDebug("📜 [script:{NodeId}] {Message}", this.nodeId, text);
                break;
            case "warning":
                this.logger.LogWarning("📜 [script:{NodeId}] {Message}", this.nodeId, text);
                break;
            case "error":
                this.logger.LogError("📜 [script:{NodeId}] {Message}", this.nodeId, text);
                break;
            default:
                this.logger.LogInformation("📜 [script:{NodeId}] {Message}", this.nodeId, text);
                break;
        }
    }

    private async Task<object?> SendAsync(HttpMethod method, string url, object? body, object? headers)
    {
        if (!this.config.AllowNetwork)
        {
            throw new ScriptSecurityException("Network access is disabled for this script (set allowNetwork=true on the node to enable).");
        }

        if (this.httpRequestCount >= this.config.MaxHttpRequests)
        {
            throw new ScriptSecurityException($"Script exceeded the maximum of {this.config.MaxHttpRequests} HTTP requests.");
        }

        this.httpRequestCount++;

        var client = this.httpClientFactory?.CreateClient("dotflow.http") ?? new HttpClient();
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            var json = body is string s ? s : JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        if (headers is IReadOnlyDictionary<string, object?> headerMap)
        {
            foreach (var (key, value) in headerMap)
            {
                request.Headers.TryAddWithoutValidation(key, value?.ToString());
            }
        }

        using var response = await client.SendAsync(request, this.cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(this.cancellationToken).ConfigureAwait(false);

        var responseHeaders = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
        {
            responseHeaders[header.Key] = string.Join(", ", header.Value);
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["status"] = (int)response.StatusCode,
            ["ok"] = response.IsSuccessStatusCode,
            ["headers"] = responseHeaders,
            ["body"] = responseBody,
        };
    }

    private void EnsureFileAllowed(string path)
    {
        if (!this.config.AllowFileSystem)
        {
            throw new ScriptSecurityException("File-system access is disabled for this script (set allowFileSystem=true on the node to enable).");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ScriptSecurityException("A file path is required.");
        }

        var full = Path.GetFullPath(path);
        var permitted = this.config.AllowedPaths.Any(allowed =>
        {
            var allowedFull = Path.GetFullPath(allowed);
            return full.Equals(allowedFull, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(allowedFull.EndsWith(Path.DirectorySeparatorChar) ? allowedFull : allowedFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        });

        if (!permitted)
        {
            throw new ScriptSecurityException($"Path '{path}' is outside the allowed paths for this script.");
        }
    }
}

/// <summary>
/// 🔧 Phase 3.1 — Construction options for <see cref="WorkflowScriptApi"/>~ ✨.
/// </summary>
public sealed class WorkflowScriptApiOptions
{
    /// <summary>Gets or sets the read-only variable snapshot.</summary>
    public IReadOnlyDictionary<string, object?>? Variables { get; set; }

    /// <summary>Gets or sets the sandbox config (already clamped).</summary>
    public ScriptExecutionConfig? Config { get; set; }

    /// <summary>Gets or sets the execution id.</summary>
    public Guid ExecutionId { get; set; }

    /// <summary>Gets or sets the workflow id.</summary>
    public Guid WorkflowId { get; set; }

    /// <summary>Gets or sets the node id.</summary>
    public string? NodeId { get; set; }

    /// <summary>Gets or sets the logger.</summary>
    public ILogger? Logger { get; set; }

    /// <summary>Gets or sets the HTTP client factory (for the gated HTTP API).</summary>
    public IHttpClientFactory? HttpClientFactory { get; set; }

    /// <summary>Gets or sets the cancellation token (timeout-linked).</summary>
    public CancellationToken CancellationToken { get; set; }
}
