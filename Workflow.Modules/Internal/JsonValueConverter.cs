// <copyright file="JsonValueConverter.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Internal;

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// 📄 Converts between <see cref="JsonNode"/> and plain CLR objects so downstream modules
/// consume a uniform dictionary/list/scalar shape~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.5.a.2. Objects → <see cref="Dictionary{TKey,TValue}"/>,
/// arrays → <see cref="List{T}"/>, scalars → string/long/double/bool/null~ 🌸.
/// </para>
/// <para>
/// CopilotNote: Both converters are <b>iterative</b> (explicit heap-allocated work stack) rather than
/// recursive, so arbitrarily deep JSON graphs can't overflow the call stack. Container children are
/// reserved in source order (dict keys / list slots pre-created) so enumeration order is preserved
/// regardless of the stack's LIFO processing order~ 🛡️.
/// </para>
/// </remarks>
public static class JsonValueConverter
{
    /// <summary>
    /// Converts a parsed <see cref="JsonNode"/> to a plain CLR object graph~ 📄.
    /// </summary>
    /// <param name="node">The node to convert (may be <c>null</c>).</param>
    /// <returns>A dictionary, list, scalar, or <c>null</c>.</returns>
    public static object? ToClr(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonValue value:
                return ScalarToClr(value);
            case not (JsonObject or JsonArray):
                return node.ToJsonString();
        }

        // Iterative walk: create the root container, then drain a work stack of pending children~ 🛡️
        var stack = new Stack<PendingNode>();
        var root = CreateNodeContainer(node, stack);

        while (stack.Count > 0)
        {
            var pending = stack.Pop();
            object? converted = pending.Node switch
            {
                null => null,
                JsonValue v => ScalarToClr(v),
                JsonObject or JsonArray => CreateNodeContainer(pending.Node, stack),
                _ => pending.Node.ToJsonString(),
            };

            pending.Assign(converted);
        }

        return root;
    }

    /// <summary>
    /// Converts a <see cref="JsonElement"/> directly to a plain CLR object graph~ 📄.
    /// </summary>
    /// <param name="element">The element to convert.</param>
    /// <returns>A dictionary, list, scalar, or <c>null</c>.</returns>
    public static object? FromElement(JsonElement element)
    {
        if (element.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
        {
            return ScalarFromElement(element);
        }

        // Iterative walk: create the root container, then drain a work stack of pending children~ 🛡️
        var stack = new Stack<PendingElement>();
        var root = CreateElementContainer(element, stack);

        while (stack.Count > 0)
        {
            var pending = stack.Pop();
            object? converted = pending.Element.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                ? CreateElementContainer(pending.Element, stack)
                : ScalarFromElement(pending.Element);

            pending.Assign(converted);
        }

        return root;
    }

    private static object CreateNodeContainer(JsonNode node, Stack<PendingNode> stack)
    {
        if (node is JsonObject obj)
        {
            var dict = new Dictionary<string, object?>();
            foreach (var kvp in obj)
            {
                dict[kvp.Key] = null; // reserve the slot in source order
                stack.Push(new PendingNode(kvp.Value, dict, kvp.Key, -1));
            }

            return dict;
        }

        var arr = (JsonArray)node;
        var list = new List<object?>(arr.Count);
        for (var i = 0; i < arr.Count; i++)
        {
            list.Add(null); // reserve the slot in source order
        }

        for (var i = 0; i < arr.Count; i++)
        {
            stack.Push(new PendingNode(arr[i], list, null, i));
        }

        return list;
    }

    private static object CreateElementContainer(JsonElement element, Stack<PendingElement> stack)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var dict = new Dictionary<string, object?>();
            foreach (var prop in element.EnumerateObject())
            {
                dict[prop.Name] = null; // reserve the slot in source order
                stack.Push(new PendingElement(prop.Value, dict, prop.Name, -1));
            }

            return dict;
        }

        var list = new List<object?>();
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            list.Add(null); // reserve the slot in source order
            stack.Push(new PendingElement(item, list, null, index));
            index++;
        }

        return list;
    }

    private static object? ScalarToClr(JsonValue value)
        => ScalarFromElement(value.GetValue<JsonElement>());

    private static object? ScalarFromElement(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.Number => element.TryGetInt64(out var l) ? (object)l : element.GetDouble(),
            _ => element.GetRawText(),
        };

    /// <summary>
    /// A pending <see cref="JsonNode"/> child + where to place its converted value~ 🧩.
    /// </summary>
    private readonly struct PendingNode
    {
        private readonly Dictionary<string, object?>? parentDict;
        private readonly List<object?>? parentList;
        private readonly string? key;
        private readonly int index;

        public PendingNode(JsonNode? node, object parent, string? key, int index)
        {
            this.Node = node;
            this.parentDict = parent as Dictionary<string, object?>;
            this.parentList = parent as List<object?>;
            this.key = key;
            this.index = index;
        }

        public JsonNode? Node { get; }

        public void Assign(object? value)
        {
            if (this.parentDict is not null)
            {
                this.parentDict[this.key!] = value;
            }
            else
            {
                this.parentList![this.index] = value;
            }
        }
    }

    /// <summary>
    /// A pending <see cref="JsonElement"/> child + where to place its converted value~ 🧩.
    /// </summary>
    private readonly struct PendingElement
    {
        private readonly Dictionary<string, object?>? parentDict;
        private readonly List<object?>? parentList;
        private readonly string? key;
        private readonly int index;

        public PendingElement(JsonElement element, object parent, string? key, int index)
        {
            this.Element = element;
            this.parentDict = parent as Dictionary<string, object?>;
            this.parentList = parent as List<object?>;
            this.key = key;
            this.index = index;
        }

        public JsonElement Element { get; }

        public void Assign(object? value)
        {
            if (this.parentDict is not null)
            {
                this.parentDict[this.key!] = value;
            }
            else
            {
                this.parentList![this.index] = value;
            }
        }
    }
}
