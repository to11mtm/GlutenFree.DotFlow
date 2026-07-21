// <copyright file="TestRunState.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Scripts.State;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Workflow.UI.Client.Api.Dtos;

/// <summary>
/// 🧪 Phase 3.4.3 — Framework-free state for the inline test runner: inputs JSON, sandbox config,
/// run status, and the last result. No Blazor/JS types (D2)~ ✨.
/// </summary>
public sealed class TestRunState
{
    /// <summary>Gets or sets the inputs JSON (surfaced to the script as <c>input</c>).</summary>
    public string Inputs { get; set; } = "{}";

    /// <summary>Gets or sets the requested timeout (clamped server-side).</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Gets or sets a value indicating whether the run may use the network.</summary>
    public bool AllowNetwork { get; set; }

    /// <summary>Gets or sets a value indicating whether the run may touch the file system.</summary>
    public bool AllowFileSystem { get; set; }

    /// <summary>Gets a value indicating whether a run is in progress.</summary>
    public bool Running { get; private set; }

    /// <summary>Gets the last successful/failed script result (a script error is a normal result).</summary>
    public ScriptTestResultDto? Result { get; private set; }

    /// <summary>Gets a transport/validation error (e.g. 422 unknown language) — distinct from a script error.</summary>
    public string? RequestError { get; private set; }

    /// <summary>Raised whenever the state changes~ 🔔.</summary>
    public event Action? Changed;

    /// <summary>Marks a run as started (clears the previous result/error)~ ▶️.</summary>
    public void BeginRun()
    {
        this.Running = true;
        this.Result = null;
        this.RequestError = null;
        this.Raise();
    }

    /// <summary>Records a completed run (success or a script error)~ ✅.</summary>
    /// <param name="result">The script result.</param>
    public void CompleteRun(ScriptTestResultDto result)
    {
        this.Running = false;
        this.Result = result;
        this.Raise();
    }

    /// <summary>Records a failed request (transport / validation)~ ❌.</summary>
    /// <param name="message">The error message.</param>
    public void FailRun(string message)
    {
        this.Running = false;
        this.RequestError = message;
        this.Raise();
    }

    /// <summary>Builds the sandbox config from the toggles~ 🔒.</summary>
    /// <returns>The config DTO.</returns>
    public ScriptTestConfigDto ToConfig()
        => new(this.TimeoutSeconds, this.AllowNetwork, this.AllowFileSystem);

    /// <summary>Parses <see cref="Inputs"/> into a dictionary (null when blank)~ 📥.</summary>
    /// <param name="error">Set to a message when the JSON is invalid.</param>
    /// <returns>The parsed inputs, or null.</returns>
    public IReadOnlyDictionary<string, JsonElement>? ParseInputs(out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(this.Inputs) || this.Inputs.Trim() == "{}")
        {
            return null;
        }

        try
        {
            var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(this.Inputs);
            return doc is { Count: > 0 } ? doc : null;
        }
        catch (JsonException ex)
        {
            error = "Inputs must be a JSON object: " + ex.Message;
            return null;
        }
    }

    /// <summary>Filters the last result's logs by level and a search substring~ 🔍.</summary>
    /// <param name="level">A level to keep (null/"All" keeps every level).</param>
    /// <param name="search">A case-insensitive substring over the message (null keeps all).</param>
    /// <returns>The filtered logs.</returns>
    public IReadOnlyList<ScriptLogEntryDto> FilteredLogs(string? level, string? search)
    {
        if (this.Result is null)
        {
            return Array.Empty<ScriptLogEntryDto>();
        }

        IEnumerable<ScriptLogEntryDto> logs = this.Result.Logs;
        if (!string.IsNullOrWhiteSpace(level) && !string.Equals(level, "All", StringComparison.OrdinalIgnoreCase))
        {
            logs = logs.Where(l => string.Equals(l.Level, level, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            logs = logs.Where(l => l.Message.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        return logs.ToList();
    }

    private void Raise() => this.Changed?.Invoke();
}
