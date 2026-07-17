// <copyright file="CompiledAssemblyKey.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq.Execution;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Workflow.Modules.Database.Abstractions;

/// <summary>
/// 🧬 Codegen version marker — bump when the compiler's codegen or allowed-types set changes so all
/// cached blobs auto-invalidate (folds into the cache key)~ ✨.
/// </summary>
public static class LinqCodegen
{
    /// <summary>The current codegen schema version. Bumped by §8.6 Phase 2 (2.4.b.P1)~.</summary>
    public const string SchemaVersion = "1";
}

/// <summary>
/// 🔑 Builds the stable <c>compiled-modules/…</c> blob key (design doc §8.3, D15)~ 💖.
/// </summary>
public static class CompiledAssemblyKey
{
    private const string Prefix = "compiled-modules";

    /// <summary>Builds the blob-key prefix for a whole definition (for eviction)~ 🗑️.</summary>
    /// <param name="definitionId">The definition id.</param>
    /// <returns>The <c>compiled-modules/{definitionId}/</c> prefix.</returns>
    public static string DefinitionPrefix(string definitionId)
        => $"{Prefix}/{Sanitize(definitionId)}/";

    /// <summary>
    /// Computes <c>compiled-modules/{definitionId}/{nodeId}/{SHA256(code+schema+tables)}.dll</c>~ 🔑.
    /// </summary>
    /// <param name="definitionId">Owning workflow definition id.</param>
    /// <param name="nodeId">The linq node id.</param>
    /// <param name="userCode">The user's method body.</param>
    /// <param name="schemaVersion">The codegen schema version.</param>
    /// <param name="selectedTables">The node's selected tables (order-independent).</param>
    /// <returns>The blob key.</returns>
    public static string Compute(
        string definitionId,
        string nodeId,
        string userCode,
        string schemaVersion,
        IReadOnlyList<WorkflowTableMetadata> selectedTables)
    {
        var payload = new StringBuilder();
        payload.Append(userCode ?? string.Empty).Append('\u0000');
        payload.Append(schemaVersion ?? string.Empty).Append('\u0000');
        payload.Append(OrderedTablesFingerprint(selectedTables));

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString())))
            .ToLowerInvariant();

        return $"{Prefix}/{Sanitize(definitionId)}/{Sanitize(nodeId)}/{hash}.dll";
    }

    // A deterministic, order-independent fingerprint of the selected tables + their column shape.
    private static string OrderedTablesFingerprint(IReadOnlyList<WorkflowTableMetadata> tables)
    {
        if (tables is null || tables.Count == 0)
        {
            return string.Empty;
        }

        var ordered = tables
            .OrderBy(t => t.TableName, StringComparer.Ordinal)
            .ThenBy(t => t.Schema ?? string.Empty, StringComparer.Ordinal);

        var sb = new StringBuilder();
        foreach (var t in ordered)
        {
            sb.Append(t.ConnectionId).Append('|')
              .Append(t.Schema ?? string.Empty).Append('|')
              .Append(t.TableName).Append('|')
              .Append(t.ClrTypeName ?? string.Empty).Append('|')
              .Append(t.AssemblyName ?? string.Empty).Append('|');

            if (t.Columns is { Count: > 0 })
            {
                foreach (var c in t.Columns.OrderBy(c => c.Name, StringComparer.Ordinal))
                {
                    sb.Append(c.Name).Append(':').Append(c.DataType).Append(':').Append(c.Nullable ? '1' : '0').Append(',');
                }
            }

            sb.Append(';');
        }

        return sb.ToString();
    }

    private static string Sanitize(string value)
        => string.IsNullOrEmpty(value)
            ? "_"
            : new string(value.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray());
}

