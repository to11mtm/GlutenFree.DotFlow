// <copyright file="StreamCollectModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Stream;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Internal;
using Workflow.Modules.Streaming;

/// <summary>
/// 🪣 Phase 5.1.1 — the <b>stream → batch</b> bridge: drains a streaming region into a single
/// array so ordinary (non-streaming) nodes downstream can work with it.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: This is the escape hatch out of the streaming world, and it is deliberately
/// <b>guarded</b> — collecting is the one operation that undoes bounded memory, so it always
/// carries a ceiling (<see cref="BoundedAccumulator"/>) and fails loudly rather than quietly
/// eating the heap~ 🛡️.
/// </para>
/// <para>
/// If you find yourself collecting a huge stream just to hand it to one batch node, the better
/// shape is usually to keep streaming all the way to a sink~ 🌊.
/// </para>
/// </remarks>
public sealed class StreamCollectModule : IStreamTerminalModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.stream.collect";

    /// <inheritdoc />
    public string DisplayName => "Collect Stream";

    /// <inheritdoc />
    public string Category => "Streaming";

    /// <inheritdoc />
    public string Description =>
        "Collects a stream of items into a single array for non-streaming nodes downstream. Guarded by an item/byte ceiling.";

    /// <inheritdoc />
    public string Icon => "🪣";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            PortDefinition.CreateStreaming("items", isRequired: true, description: "The item stream to collect")),
        Outputs: Arr.create(
            new PortDefinition(
                Name: "items",
                DisplayName: "Items",
                DataType: typeof(IReadOnlyList<object?>),
                Description: "Every collected item, in arrival order",
                IsRequired: false),
            new PortDefinition(
                Name: "count",
                DisplayName: "Count",
                DataType: typeof(long),
                Description: "How many items were collected",
                IsRequired: false)),
        Properties: Arr.create(
            new ModulePropertyDefinition(
                Name: "maxItems",
                DisplayName: "Max items",
                DataType: typeof(int),
                Description: "Fail if more than this many items arrive. Keeps a collect from eating the heap.",
                IsRequired: false,
                DefaultValue: BoundedAccumulator.DefaultMaxItems,
                EditorType: PropertyEditorType.Number),
            new ModulePropertyDefinition(
                Name: "maxBytes",
                DisplayName: "Max bytes",
                DataType: typeof(long),
                Description: "Optional payload-size ceiling in bytes. Leave empty to skip byte measurement.",
                IsRequired: false,
                EditorType: PropertyEditorType.Number)));

    /// <inheritdoc />
    public async Task<ModuleResult> ExecuteTerminalAsync(
        ModuleExecutionContext context,
        IAsyncEnumerable<StreamItem> input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        var policy = ReadPolicy(context, out var policyError);
        if (policyError is not null)
        {
            return ModuleResult.Fail(policyError);
        }

        var collected = new List<object?>();

        try
        {
            var accumulator = new BoundedAccumulator(policy);

            await foreach (var item in input.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                accumulator.Admit(item, context.NodeId);
                collected.Add(ToValue(item));
            }

            context.Logger.LogInformation(
                "🪣 Collected {Count} items at node {NodeId}~",
                collected.Count,
                context.NodeId);

            return ModuleResult.Ok(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["items"] = collected,
                ["count"] = (long)collected.Count,
            });
        }
        catch (StreamAccumulatorLimitException ex)
        {
            return ModuleResult.Fail(ex.Message, ex);
        }
        catch (NotSupportedException ex)
        {
            return ModuleResult.Fail(ex.Message, ex);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Collect only makes sense at the edge of a streaming region. Failing here — rather than
    /// silently passing a value through — is what stops a mis-wired graph from looking healthy~ 🚧.
    /// </remarks>
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ModuleResult.Fail(
            "builtin.stream.collect needs a streaming connection into its 'items' port — wire it to a streaming output, or drop the node if the data is already an array~ 🪣"));

    /// <inheritdoc />
    public IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
        ModuleExecutionContext context,
        IAsyncEnumerable<StreamItem> input,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "builtin.stream.collect is a terminal stage — the region executor calls ExecuteTerminalAsync~ 🪣");

    private static object? ToValue(StreamItem item)
        => item.Payload switch
        {
            JsonPayload json => JsonValueConverter.FromElement(json.Value),
            _ => null,
        };

    private static BoundedAccumulatorPolicy ReadPolicy(ModuleExecutionContext context, out string? error)
    {
        error = null;
        var maxItems = BoundedAccumulator.DefaultMaxItems;
        long? maxBytes = null;

        if (context.Properties.TryGetValue("maxItems", out var rawMaxItems) && rawMaxItems is not null)
        {
            if (!TryReadInt64(rawMaxItems, out var parsed) || parsed <= 0)
            {
                error = "'maxItems' must be a positive whole number~ 💔";
                return BoundedAccumulatorPolicy.Default;
            }

            maxItems = (int)Math.Min(parsed, int.MaxValue);
        }

        if (context.Properties.TryGetValue("maxBytes", out var rawMaxBytes) && rawMaxBytes is not null)
        {
            if (!TryReadInt64(rawMaxBytes, out var parsedBytes) || parsedBytes <= 0)
            {
                error = "'maxBytes' must be a positive whole number~ 💔";
                return BoundedAccumulatorPolicy.Default;
            }

            maxBytes = parsedBytes;
        }

        return new BoundedAccumulatorPolicy(maxItems, maxBytes);
    }

    private static bool TryReadInt64(object raw, out long value)
    {
        switch (raw)
        {
            case long l:
                value = l;
                return true;
            case int i:
                value = i;
                return true;
            case double d when d == Math.Floor(d):
                value = (long)d;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt64(out var fromJson):
                value = fromJson;
                return true;
            case string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                value = parsed;
                return true;
            default:
                value = 0;
                return false;
        }
    }
}
