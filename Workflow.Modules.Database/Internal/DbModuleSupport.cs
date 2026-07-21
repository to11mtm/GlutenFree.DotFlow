// <copyright file="DbModuleSupport.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Internal;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB.Data;
using Workflow.Core.Models;
using Workflow.Modules.Database.Abstractions;

/// <summary>
/// 🧰 Shared plumbing for the database module family (2.4.a) — property readers,
/// connection-source validation, and named-or-raw connection resolution~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.4.a.2 extracted this from <c>DatabaseQueryModule</c> so query,
/// execute, transaction, and bulkinsert all share ONE copy of the boring bits. Keep this
/// to config + connection concerns only — no SQL execution logic lives here (that stays
/// in each module so their control flow reads top-to-bottom)~ 🌸.
/// </remarks>
public static class DbModuleSupport
{
    /// <summary>
    /// Reads a string property (via <see cref="object.ToString"/>), or <see langword="null"/>~ 🏷️.
    /// </summary>
    public static string? GetString(IReadOnlyDictionary<string, object?> props, string key)
        => props.TryGetValue(key, out var v) && v is not null ? v.ToString() : null;

    /// <summary>
    /// Parses an int property from the common shapes JSON deserialisation produces~ 🔢.
    /// </summary>
    public static int? TryParseInt(IReadOnlyDictionary<string, object?> props, string key)
    {
        if (!props.TryGetValue(key, out var v) || v is null)
        {
            return null;
        }

        return v switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => null,
        };
    }

    /// <summary>
    /// Parses a bool property — handles bool, "true"/"false" strings, and 0/1 integers~ ✅.
    /// </summary>
    public static bool? TryParseBool(IReadOnlyDictionary<string, object?> props, string key)
    {
        if (!props.TryGetValue(key, out var v) || v is null)
        {
            return null;
        }

        return v switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            int i => i != 0,
            long l => l != 0,
            _ => null,
        };
    }

    /// <summary>
    /// Appends connection-source validation errors (the <c>connectionId</c> XOR
    /// <c>connectionString</c>+<c>provider</c> rule, D3) to <paramref name="errors"/>~ 🔀.
    /// </summary>
    /// <param name="config">The module configuration.</param>
    /// <param name="errors">The collector to append validation errors to.</param>
    public static void ValidateConnectionSource(
        IReadOnlyDictionary<string, object?> config,
        ICollection<ValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var hasConnectionId = !string.IsNullOrWhiteSpace(GetString(config, "connectionId"));
        var hasConnectionString = !string.IsNullOrWhiteSpace(GetString(config, "connectionString"));
        var hasProvider = !string.IsNullOrWhiteSpace(GetString(config, "provider"));

        if (hasConnectionId && hasConnectionString)
        {
            errors.Add(new ValidationError(
                "DB_CONNECTION_AMBIGUOUS",
                "Set exactly one of 'connectionId' or 'connectionString' — not both~ 💔",
                PropertyName: "connectionId"));
        }
        else if (!hasConnectionId && !hasConnectionString)
        {
            errors.Add(new ValidationError(
                "DB_CONNECTION_MISSING",
                "One of 'connectionId' (preferred) or 'connectionString' + 'provider' is required~ 💔",
                PropertyName: "connectionId"));
        }
        else if (hasConnectionString && !hasProvider)
        {
            errors.Add(new ValidationError(
                "DB_PROVIDER_REQUIRED",
                "'provider' is required when using a raw 'connectionString'~ 💔",
                PropertyName: "provider"));
        }
    }

    /// <summary>
    /// Resolves a <see cref="DataConnection"/> from module properties — named (preferred)
    /// or raw (provider + connection string). The caller owns disposal (<c>using</c>)~ 🔌.
    /// </summary>
    /// <param name="factory">The shared connection factory.</param>
    /// <param name="props">The module configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A ready-to-use <see cref="DataConnection"/>.</returns>
    public static ValueTask<DataConnection> CreateConnectionAsync(
        IDbConnectionFactory factory,
        IReadOnlyDictionary<string, object?> props,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var connectionId = GetString(props, "connectionId");
        return string.IsNullOrWhiteSpace(connectionId)
            ? factory.CreateAsync(GetString(props, "provider")!, GetString(props, "connectionString")!, ct)
            : factory.CreateAsync(connectionId, ct);
    }
}
