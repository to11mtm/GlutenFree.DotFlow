// <copyright file="SecretRedactor.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Actors;

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Workflow.Core.Models;

/// <summary>
/// 🔒 §10.6 — hides the values of variables declared <c>IsSecret</c> before anything leaves the
/// engine for a place people can read: run history (execution and node records), the run's own
/// variable-write records, debug logs and the failure events sent to live clients.
/// </summary>
/// <remarks>
/// <para>
/// Policy (decided 2026-11-08): redact by <b>name and value</b>. A key equal to a secret variable's
/// name is masked, and the secret's current value is replaced wherever it appears inside a string
/// (a URL, a header, an echoed output). Values shorter than <see cref="MinValueLength"/> characters
/// are masked by name only, so a secret like "1" can't blank out unrelated text.
/// </para>
/// <para>
/// Not covered by design (pivot options in plan 03 Finding D): the workflow/global variable store
/// ("Save for future runs") and state snapshots — storage at rest, which resume needs verbatim.
/// </para>
/// <para>Immutable: <see cref="With"/> returns a new instance, so it can be shared across actors.</para>
/// </remarks>
public sealed class SecretRedactor
{
    /// <summary>What a hidden value is replaced with.</summary>
    public const string Mask = "••••••";

    /// <summary>Shorter secret values are only masked by name, never searched for in text.</summary>
    public const int MinValueLength = 4;

    /// <summary>A redactor that changes nothing (no secrets declared).</summary>
    public static readonly SecretRedactor None = new(ImmutableHashSet<string>.Empty.WithComparer(StringComparer.OrdinalIgnoreCase), ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal));

    private readonly ImmutableHashSet<string> names;
    private readonly ImmutableDictionary<string, string> values;
    private readonly string[] searchValues;

    private SecretRedactor(ImmutableHashSet<string> names, ImmutableDictionary<string, string> values)
    {
        this.names = names;
        this.values = values;

        // Longest first, so a secret that contains another is replaced whole~
        this.searchValues = values.Values
            .Where(v => v.Length >= MinValueLength)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(v => v.Length)
            .ToArray();
    }

    /// <summary>Gets a value indicating whether there is nothing to hide.</summary>
    public bool IsEmpty => this.names.IsEmpty;

    /// <summary>Builds the redactor for a run: the declared secret names and their current values.</summary>
    /// <param name="definition">The workflow (declares which variables are secret).</param>
    /// <param name="variables">The run's variables as resolved (declared, stored, run inputs).</param>
    /// <param name="storedSecretNames">
    /// 🔒 Plan 08 — names of stored (global / workflow-scope) variables marked secret, hidden too even
    /// when this workflow doesn't declare them.
    /// </param>
    /// <returns>The redactor (<see cref="None"/> when nothing is secret).</returns>
    public static SecretRedactor For(
        WorkflowDefinition definition,
        IEnumerable<KeyValuePair<string, object?>>? variables,
        IEnumerable<string>? storedSecretNames = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var secretNames = definition.Variables.Values
            .Where(v => v.IsSecret)
            .Select(v => v.Name)
            .Concat(storedSecretNames ?? Enumerable.Empty<string>())
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        if (secretNames.IsEmpty)
        {
            return None;
        }

        var redactor = new SecretRedactor(secretNames, ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal));
        foreach (var (name, value) in variables ?? Enumerable.Empty<KeyValuePair<string, object?>>())
        {
            redactor = redactor.With(name, value);
        }

        return redactor;
    }

    /// <summary>Learns a secret variable's new value (no-op for a non-secret name).</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">Its value.</param>
    /// <returns>This redactor, or a new one that also hides the new value.</returns>
    /// <remarks>The previous value stays hidden too — it may already be sitting in earlier records.</remarks>
    public SecretRedactor With(string name, object? value)
    {
        if (!this.names.Contains(name) || TextOf(value) is not { Length: > 0 } text)
        {
            return this;
        }

        var key = $"{name}\u0000{text}";
        return this.values.ContainsKey(key)
            ? this
            : new SecretRedactor(this.names, this.values.SetItem(key, text));
    }

    /// <summary>Whether a key names a secret variable.</summary>
    /// <param name="name">The key.</param>
    /// <returns>True when masked by name.</returns>
    public bool IsSecretName(string? name) => name is not null && this.names.Contains(name);

    /// <summary>Hides secret values inside a piece of text (error messages, log text).</summary>
    /// <param name="text">The text.</param>
    /// <returns>The text with every secret value replaced by <see cref="Mask"/>.</returns>
    public string? Text(string? text)
    {
        if (string.IsNullOrEmpty(text) || this.searchValues.Length == 0)
        {
            return text;
        }

        foreach (var secret in this.searchValues)
        {
            if (text.Contains(secret, StringComparison.Ordinal))
            {
                text = text.Replace(secret, Mask, StringComparison.Ordinal);
            }
        }

        return text;
    }

    /// <summary>Hides secrets in a key/value map: secret-named keys are masked, values are searched.</summary>
    /// <param name="map">The map (null passes through).</param>
    /// <returns>A new map, or the same instance when there is nothing to hide.</returns>
    public Dictionary<string, object?>? Map(IReadOnlyDictionary<string, object?>? map)
    {
        if (map is null)
        {
            return null;
        }

        var copy = new Dictionary<string, object?>(map.Count, StringComparer.Ordinal);
        foreach (var (key, value) in map)
        {
            copy[key] = this.IsEmpty ? value : this.IsSecretName(key) ? Mask : this.Value(value);
        }

        return copy;
    }

    /// <summary>Hides secrets anywhere inside a value (strings, maps, lists, JSON).</summary>
    /// <param name="value">The value.</param>
    /// <returns>The value, redacted.</returns>
    public object? Value(object? value)
    {
        if (this.IsEmpty || value is null)
        {
            return value;
        }

        switch (value)
        {
            case string s:
                return this.Text(s);

            case JsonElement json:
                return this.Json(json);

            case IReadOnlyDictionary<string, object?> map:
                return this.Map(map);

            case IDictionary dictionary:
                var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (DictionaryEntry entry in dictionary)
                {
                    var key = entry.Key?.ToString() ?? string.Empty;
                    copy[key] = this.IsSecretName(key) ? Mask : this.Value(entry.Value);
                }

                return copy;

            case byte[]:
                return value;

            case IEnumerable sequence:
                return sequence.Cast<object?>().Select(this.Value).ToList();

            default:
                return value;
        }
    }

    private static string? TextOf(object? value) => value switch
    {
        null => null,
        string s => s,
        JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
        JsonElement { ValueKind: JsonValueKind.Undefined or JsonValueKind.Null } => null,
        JsonElement json => json.GetRawText(),
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private JsonElement Json(JsonElement json)
    {
        if (json.ValueKind is JsonValueKind.String)
        {
            var text = json.GetString();
            var redacted = this.Text(text);
            return ReferenceEquals(text, redacted) || text == redacted ? json : JsonSerializer.SerializeToElement(redacted);
        }

        if (json.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
        {
            return json;
        }

        var node = JsonNode.Parse(json.GetRawText());
        this.Walk(node);
        return JsonSerializer.SerializeToElement(node);
    }

    private void Walk(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, child) in obj.ToList())
                {
                    if (this.IsSecretName(key))
                    {
                        obj[key] = Mask;
                    }
                    else if (child is JsonValue v && v.TryGetValue<string>(out var s))
                    {
                        obj[key] = this.Text(s);
                    }
                    else
                    {
                        this.Walk(child);
                    }
                }

                break;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue v && v.TryGetValue<string>(out var s))
                    {
                        array[i] = this.Text(s);
                    }
                    else
                    {
                        this.Walk(array[i]);
                    }
                }

                break;
        }
    }
}

/// <summary>
/// 🔒 §10.6 — the current <see cref="SecretRedactor"/> per running execution, so actors spawned for
/// construct bodies (<see cref="SubGraphExecutor"/>) redact with the same knowledge as the
/// <see cref="WorkflowExecutor"/> without threading it through every construct.
/// </summary>
public static class ExecutionSecrets
{
    private static readonly ConcurrentDictionary<Guid, SecretRedactor> Redactors = new();

    /// <summary>Gets the execution's redactor (<see cref="SecretRedactor.None"/> when unknown).</summary>
    /// <param name="executionId">The execution.</param>
    /// <returns>The redactor.</returns>
    public static SecretRedactor For(Guid executionId)
        => Redactors.TryGetValue(executionId, out var redactor) ? redactor : SecretRedactor.None;

    /// <summary>Sets the execution's redactor.</summary>
    /// <param name="executionId">The execution.</param>
    /// <param name="redactor">The redactor.</param>
    public static void Set(Guid executionId, SecretRedactor redactor) => Redactors[executionId] = redactor;

    /// <summary>Learns a variable write (a no-op unless the variable is secret).</summary>
    /// <param name="executionId">The execution.</param>
    /// <param name="name">The variable name.</param>
    /// <param name="value">Its new value.</param>
    /// <returns>The updated redactor.</returns>
    public static SecretRedactor Learn(Guid executionId, string name, object? value)
    {
        while (Redactors.TryGetValue(executionId, out var current))
        {
            var next = current.With(name, value);
            if (ReferenceEquals(next, current) || Redactors.TryUpdate(executionId, next, current))
            {
                return next;
            }
        }

        return SecretRedactor.None;
    }

    /// <summary>
    /// Returns the error with secret values hidden from its message — the same instance when
    /// nothing changed; a cancellation stays a cancellation (subscribers tell cancel from failure).
    /// </summary>
    /// <param name="redactor">The redactor.</param>
    /// <param name="error">The error.</param>
    /// <returns>The error to publish.</returns>
    public static Exception Error(SecretRedactor redactor, Exception error)
    {
        var message = redactor.Text(error.Message);
        if (message == error.Message)
        {
            return error;
        }

        return error is OperationCanceledException
            ? new OperationCanceledException(message)
            : new RedactedException(message!, error.GetType().FullName ?? error.GetType().Name);
    }

    /// <summary>Forgets an execution when its executor stops.</summary>
    /// <param name="executionId">The execution.</param>
    public static void Remove(Guid executionId) => Redactors.TryRemove(executionId, out _);
}

/// <summary>🔒 §10.6 — stands in for an error whose message held a secret value.</summary>
public sealed class RedactedException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="RedactedException"/> class.</summary>
    /// <param name="message">The redacted message.</param>
    /// <param name="originalType">The original exception's type name.</param>
    public RedactedException(string message, string originalType)
        : base(message) => this.OriginalType = originalType;

    /// <summary>Gets the original exception's type name.</summary>
    public string OriginalType { get; }
}
