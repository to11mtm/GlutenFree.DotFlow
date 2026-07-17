// <copyright file="SqlTypeMapper.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq.Compilation;

using System;
using System.Collections.Generic;

/// <summary>
/// 🗺️ Maps a provider-reported SQL type string (from <c>WorkflowColumnMetadata.DataType</c>) to a
/// C# type name for column-generated POCOs (2.4.b.1 dual-POCO path)~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: Covers the common Postgres + SQLite type names. Unknown types fall back to
/// <c>object?</c> (with a warning raised by the caller). Extended per-provider maps land alongside
/// MySQL/SQL Server in 2.4.a.P3 / 2.4.b.P1~ 🌸.
/// </remarks>
public static class SqlTypeMapper
{
    // Base (non-nullable) C# type name keyed by a normalised SQL type token.
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // integers
        ["int"] = "int",
        ["int4"] = "int",
        ["integer"] = "int",
        ["serial"] = "int",
        ["smallint"] = "short",
        ["int2"] = "short",
        ["bigint"] = "long",
        ["int8"] = "long",
        ["bigserial"] = "long",

        // text
        ["text"] = "string",
        ["varchar"] = "string",
        ["character varying"] = "string",
        ["char"] = "string",
        ["character"] = "string",
        ["nvarchar"] = "string",
        ["clob"] = "string",
        ["uuid"] = "global::System.Guid",

        // booleans
        ["bool"] = "bool",
        ["boolean"] = "bool",

        // floating / decimal
        ["real"] = "double",
        ["float4"] = "double",
        ["float8"] = "double",
        ["double"] = "double",
        ["double precision"] = "double",
        ["numeric"] = "decimal",
        ["decimal"] = "decimal",
        ["money"] = "decimal",

        // temporal
        ["date"] = "global::System.DateTime",
        ["timestamp"] = "global::System.DateTime",
        ["timestamp without time zone"] = "global::System.DateTime",
        ["datetime"] = "global::System.DateTime",
        ["timestamptz"] = "global::System.DateTimeOffset",
        ["timestamp with time zone"] = "global::System.DateTimeOffset",
        ["time"] = "global::System.TimeSpan",
        ["interval"] = "global::System.TimeSpan",

        // binary
        ["bytea"] = "byte[]",
        ["blob"] = "byte[]",
    };

    private static readonly HashSet<string> ValueTypes = new(StringComparer.Ordinal)
    {
        "int", "short", "long", "bool", "double", "decimal",
        "global::System.Guid", "global::System.DateTime",
        "global::System.DateTimeOffset", "global::System.TimeSpan",
    };

    /// <summary>
    /// Maps a SQL type token to a C# type name, applying nullability~ 🎯.
    /// </summary>
    /// <param name="sqlType">The provider-reported type (e.g. <c>"integer"</c>, <c>"timestamptz"</c>).</param>
    /// <param name="nullable">Whether the column allows NULL.</param>
    /// <param name="csharpName">The emitted C# type name.</param>
    /// <returns><c>true</c> when the SQL type was recognised; <c>false</c> when it fell back to <c>object?</c>.</returns>
    public static bool TryMap(string? sqlType, bool nullable, out string csharpName)
    {
        var token = Normalise(sqlType);
        if (token is null || !Map.TryGetValue(token, out var baseName))
        {
            csharpName = "object?";
            return false;
        }

        csharpName = nullable
            ? (ValueTypes.Contains(baseName) ? baseName + "?" : baseName + "?")
            : baseName;
        return true;
    }

    // Strips length/precision qualifiers, e.g. "numeric(12,4)" → "numeric", "varchar(255)" → "varchar".
    private static string? Normalise(string? sqlType)
    {
        if (string.IsNullOrWhiteSpace(sqlType))
        {
            return null;
        }

        var t = sqlType.Trim();
        var paren = t.IndexOf('(');
        if (paren >= 0)
        {
            t = t[..paren].Trim();
        }

        return t.ToLowerInvariant();
    }
}

