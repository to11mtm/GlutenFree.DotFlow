// <copyright file="ConnectionStringComposer.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Linq.State;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>The kind of input a connection field wants~ 🎛️.</summary>
public enum ConnectionFieldKind
{
    /// <summary>A free-text value.</summary>
    Text,

    /// <summary>A masked secret.</summary>
    Secret,

    /// <summary>A whole number.</summary>
    Number,

    /// <summary>A fixed set of values.</summary>
    Select,
}

/// <summary>
/// 📇 One provider-specific connection field — label, keyword, and how to render it~ ✨.
/// </summary>
/// <param name="Key">The connection-string keyword this field writes (e.g. <c>Host</c>).</param>
/// <param name="Label">The human label.</param>
/// <param name="Kind">How to render the input.</param>
/// <param name="Required">Whether a value must be supplied.</param>
/// <param name="Placeholder">An example value.</param>
/// <param name="Default">The default value (also used as the placeholder for optional fields).</param>
/// <param name="Options">The allowed values for <see cref="ConnectionFieldKind.Select"/>.</param>
/// <param name="Help">A short hint shown under the field.</param>
public sealed record ConnectionField(
    string Key,
    string Label,
    ConnectionFieldKind Kind,
    bool Required = false,
    string? Placeholder = null,
    string? Default = null,
    IReadOnlyList<string>? Options = null,
    string? Help = null);

/// <summary>
/// 📇 Linq Studio (L6) — provider-aware connection-string composition. Instead of asking users to
/// hand-write ADO.NET connection strings, each provider exposes a guided field set that composes
/// (and round-trips back from) a connection string. Framework-free (D2) so it's unit-testable~ ✨.
/// </summary>
public static class ConnectionStringComposer
{
    private static readonly IReadOnlyList<ConnectionField> PostgresFields = new List<ConnectionField>
    {
        new("Host", "Host", ConnectionFieldKind.Text, Required: true, Placeholder: "localhost"),
        new("Port", "Port", ConnectionFieldKind.Number, Default: "5432"),
        new("Database", "Database", ConnectionFieldKind.Text, Required: true, Placeholder: "dotflow"),
        new("Username", "Username", ConnectionFieldKind.Text, Required: true, Placeholder: "postgres"),
        new("Password", "Password", ConnectionFieldKind.Secret),
        new(
            "SSL Mode",
            "SSL mode",
            ConnectionFieldKind.Select,
            Default: "Prefer",
            Options: new[] { "Disable", "Allow", "Prefer", "Require", "VerifyCA", "VerifyFull" },
            Help: "Use Require or stronger for anything outside localhost."),
        new("Pooling", "Pooling", ConnectionFieldKind.Select, Default: "true", Options: new[] { "true", "false" }),
        new("Timeout", "Connect timeout (s)", ConnectionFieldKind.Number, Default: "15"),
    };

    private static readonly IReadOnlyList<ConnectionField> SqliteFields = new List<ConnectionField>
    {
        new(
            "Data Source",
            "Database file",
            ConnectionFieldKind.Text,
            Required: true,
            Placeholder: "./data/dotflow.db",
            Help: "A file path relative to the server, or ':memory:' for an in-process database."),
        new(
            "Mode",
            "Mode",
            ConnectionFieldKind.Select,
            Default: "ReadWriteCreate",
            Options: new[] { "ReadWriteCreate", "ReadWrite", "ReadOnly", "Memory" }),
        new("Cache", "Cache", ConnectionFieldKind.Select, Default: "Default", Options: new[] { "Default", "Private", "Shared" }),
        new("Foreign Keys", "Enforce foreign keys", ConnectionFieldKind.Select, Default: "True", Options: new[] { "True", "False" }),
        new("Password", "Password (SQLCipher)", ConnectionFieldKind.Secret),
    };

    /// <summary>Gets the provider keys the guided builder understands~ 🗂️.</summary>
    public static IReadOnlyList<string> Providers { get; } = new[] { "postgres", "sqlite" };

    /// <summary>Gets a friendly label for a provider key~ 🏷️.</summary>
    /// <param name="providerKey">The provider key.</param>
    /// <returns>The display label.</returns>
    public static string ProviderLabel(string? providerKey)
        => Normalize(providerKey) switch
        {
            "postgres" => "PostgreSQL",
            "sqlite" => "SQLite",
            _ => providerKey ?? string.Empty,
        };

    /// <summary>Gets the guided fields for a provider (empty when unknown → raw entry)~ 🎛️.</summary>
    /// <param name="providerKey">The provider key ("postgres"/"sqlite").</param>
    /// <returns>The field set, or an empty list for providers without a guided form.</returns>
    public static IReadOnlyList<ConnectionField> FieldsFor(string? providerKey)
        => Normalize(providerKey) switch
        {
            "postgres" => PostgresFields,
            "sqlite" => SqliteFields,
            _ => Array.Empty<ConnectionField>(),
        };

    /// <summary>Seeds a value map with each field's default~ 🌱.</summary>
    /// <param name="providerKey">The provider key.</param>
    /// <returns>A mutable map of keyword → default value.</returns>
    public static Dictionary<string, string?> Defaults(string? providerKey)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in FieldsFor(providerKey))
        {
            map[f.Key] = f.Default;
        }

        return map;
    }

    /// <summary>
    /// Lists the required fields that are still empty — the guided form's validation~ 🚦.
    /// </summary>
    /// <param name="providerKey">The provider key.</param>
    /// <param name="values">The current field values.</param>
    /// <returns>The labels of the missing required fields.</returns>
    public static IReadOnlyList<string> MissingRequired(string? providerKey, IReadOnlyDictionary<string, string?> values)
    {
        if (values is null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        return FieldsFor(providerKey)
            .Where(f => f.Required && (!values.TryGetValue(f.Key, out var v) || string.IsNullOrWhiteSpace(v)))
            .Select(f => f.Label)
            .ToList();
    }

    /// <summary>
    /// Composes a connection string from the guided field values. Empty values and values equal to
    /// the field default are omitted to keep the string tidy; values containing <c>;</c> or
    /// <c>"</c> are quoted per ADO.NET rules~ 🧵.
    /// </summary>
    /// <param name="providerKey">The provider key.</param>
    /// <param name="values">The field values.</param>
    /// <returns>The composed connection string.</returns>
    public static string Build(string? providerKey, IReadOnlyDictionary<string, string?> values)
    {
        if (values is null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        var parts = new List<string>();
        foreach (var f in FieldsFor(providerKey))
        {
            if (!values.TryGetValue(f.Key, out var raw))
            {
                raw = f.Default;
            }

            var value = raw?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            // Optional fields left at their default add noise — keep them implicit.
            if (!f.Required && f.Default is not null && string.Equals(value, f.Default, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            parts.Add($"{f.Key}={Quote(value)}");
        }

        return string.Join(";", parts);
    }

    /// <summary>
    /// Parses a connection string back into guided field values (unknown keywords are dropped —
    /// the caller should keep the raw string when round-tripping matters)~ 🔎.
    /// </summary>
    /// <param name="providerKey">The provider key.</param>
    /// <param name="connectionString">The connection string.</param>
    /// <returns>A map of keyword → value seeded with defaults.</returns>
    public static Dictionary<string, string?> Parse(string? providerKey, string? connectionString)
    {
        var map = Defaults(providerKey);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return map;
        }

        foreach (var segment in SplitSegments(connectionString!))
        {
            var eq = segment.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            var key = segment[..eq].Trim();
            var value = Unquote(segment[(eq + 1)..].Trim());
            if (map.ContainsKey(key))
            {
                map[key] = value;
            }
        }

        return map;
    }

    private static IEnumerable<string> SplitSegments(string connectionString)
    {
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        char quoteChar = '\0';
        foreach (var ch in connectionString)
        {
            if (inQuotes)
            {
                if (ch == quoteChar)
                {
                    inQuotes = false;
                }

                current.Append(ch);
                continue;
            }

            if (ch is '\'' or '"')
            {
                inQuotes = true;
                quoteChar = ch;
                current.Append(ch);
                continue;
            }

            if (ch == ';')
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static string Quote(string value)
    {
        if (!value.Contains(';', StringComparison.Ordinal) &&
            !value.Contains('"', StringComparison.Ordinal) &&
            !value.Contains('\'', StringComparison.Ordinal))
        {
            return value;
        }

        return value.Contains('"', StringComparison.Ordinal)
            ? "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'"
            : "\"" + value + "\"";
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            var inner = value[1..^1];
            return value[0] == '\''
                ? inner.Replace("''", "'", StringComparison.Ordinal)
                : inner;
        }

        return value;
    }

    private static string Normalize(string? providerKey)
        => providerKey?.Trim().ToLowerInvariant() ?? string.Empty;
}
