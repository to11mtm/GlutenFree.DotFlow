// <copyright file="TableDesignerTypes.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Linq.State;

using System;
using System.Collections.Generic;

/// <summary>
/// 📐 The column-type choices the table designer offers, per provider — a user defining an Oracle
/// table should pick <c>NUMBER</c>/<c>VARCHAR2</c>, not Postgres' <c>text</c>. The values are the
/// provider-reported type names the POCO generator maps from. Framework-free (D2)~ ✨.
/// </summary>
public static class TableDesignerTypes
{
    private static readonly IReadOnlyList<string> Ansi = new[]
    {
        "text", "integer", "bigint", "numeric", "boolean", "timestamp", "uuid",
    };

    private static readonly IReadOnlyList<string> Postgres = new[]
    {
        "text", "varchar", "integer", "bigint", "smallint", "numeric", "double precision",
        "boolean", "date", "timestamp", "timestamp with time zone", "uuid", "bytea",
    };

    private static readonly IReadOnlyList<string> Sqlite = new[]
    {
        "TEXT", "INTEGER", "REAL", "NUMERIC", "BLOB",
    };

    private static readonly IReadOnlyList<string> Oracle = new[]
    {
        "NUMBER", "VARCHAR2", "NVARCHAR2", "CHAR", "CLOB", "NCLOB",
        "BINARY_FLOAT", "BINARY_DOUBLE", "DATE", "TIMESTAMP",
        "TIMESTAMP WITH TIME ZONE", "RAW", "BLOB",
    };

    /// <summary>Gets the type list for a provider (a generic set when unknown)~ 🗂️.</summary>
    /// <param name="providerKey">The provider key ("postgres"/"sqlite"/"oracle").</param>
    /// <returns>The offered column types, most common first.</returns>
    public static IReadOnlyList<string> For(string? providerKey)
        => (providerKey?.Trim().ToLowerInvariant()) switch
        {
            "postgres" => Postgres,
            "sqlite" => Sqlite,
            "oracle" => Oracle,
            _ => Ansi,
        };

    /// <summary>Gets the default column type for a provider (the first offered)~ 🌱.</summary>
    /// <param name="providerKey">The provider key.</param>
    /// <returns>The default type token.</returns>
    public static string DefaultFor(string? providerKey) => For(providerKey)[0];

    /// <summary>
    /// Keeps a chosen type usable when the provider changes: an exact (case-insensitive) match is
    /// kept, otherwise the provider's default is used~ 🔁.
    /// </summary>
    /// <param name="providerKey">The target provider key.</param>
    /// <param name="currentType">The currently selected type.</param>
    /// <returns>A type valid for the provider.</returns>
    public static string Coerce(string? providerKey, string? currentType)
    {
        foreach (var t in For(providerKey))
        {
            if (string.Equals(t, currentType, StringComparison.OrdinalIgnoreCase))
            {
                return t;
            }
        }

        return DefaultFor(providerKey);
    }
}
