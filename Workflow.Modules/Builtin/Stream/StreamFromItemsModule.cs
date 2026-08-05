// <copyright file="StreamFromItemsModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Stream;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🚰 Phase 5.1.1 — the <b>batch → stream</b> bridge: turns an array that's already in memory into
/// an item stream, so the streaming stages downstream can do their work.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: This does <b>not</b> make anything memory-bounded — the array is already loaded.
/// It exists so a streaming region can start from data produced by ordinary nodes (a script's
/// output, an HTTP response body, a small query). For genuinely large data, start the region with
/// a streaming <i>source</i> instead~ 🌊.
/// </para>
/// </remarks>
public sealed class StreamFromItemsModule : IStreamingWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.stream.fromitems";

    /// <inheritdoc />
    public string DisplayName => "Stream From Items";

    /// <inheritdoc />
    public string Category => "Streaming";

    /// <inheritdoc />
    public string Description =>
        "Turns an in-memory array into a stream of items so streaming nodes can process them one at a time.";

    /// <inheritdoc />
    public string Icon => "🚰";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition(
                Name: "items",
                DisplayName: "Items",
                DataType: typeof(IEnumerable<object?>),
                Description: "The array to stream",
                IsRequired: false)),
        Outputs: Arr.create(
            PortDefinition.CreateStreaming("items", isRequired: false, description: "One item per array element")),
        Properties: Arr<ModulePropertyDefinition>.Empty);

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
        ModuleExecutionContext context,
        IAsyncEnumerable<StreamItem> input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var source = context.Inputs.TryGetValue("items", out var fromPort) && fromPort is not null
            ? fromPort
            : context.Properties.TryGetValue("items", out var fromProperty) ? fromProperty : null;

        if (source is null)
        {
            context.Logger.LogWarning(
                "🚰 Node {NodeId} had no 'items' to stream — emitting an empty stream~",
                context.NodeId);
            yield break;
        }

        long index = 0;

        foreach (var element in Enumerate(source))
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return StreamItem.FromJson(
                ToJsonElement(element),
                index,
                new SourceOffset(index.ToString(System.Globalization.CultureInfo.InvariantCulture), index));

            index++;
        }

        // Async iterators need at least one await to avoid a synchronous-completion warning, and it
        // gives the scheduler a breathing point between large arrays~ 🌸
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Only meaningful as the head of a streaming region — failing loudly beats emitting a value
    /// nobody downstream can consume~ 🚧.
    /// </remarks>
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ModuleResult.Fail(
            "builtin.stream.fromitems needs its streaming 'items' output wired to a streaming input — otherwise the array never goes anywhere~ 🚰"));

    private static IEnumerable<object?> Enumerate(object source)
    {
        switch (source)
        {
            case JsonElement { ValueKind: JsonValueKind.Array } array:
                foreach (var element in array.EnumerateArray())
                {
                    yield return element;
                }

                break;

            case string single:
                // A string is IEnumerable<char>, which is never what an author means here~ 🎀
                yield return single;
                break;

            case IEnumerable enumerable:
                foreach (var element in enumerable)
                {
                    yield return element;
                }

                break;

            default:
                yield return source;
                break;
        }
    }

    private static JsonElement ToJsonElement(object? value)
        => value switch
        {
            JsonElement element => element,
            null => default,
            _ => JsonSerializer.SerializeToElement(value),
        };
}
