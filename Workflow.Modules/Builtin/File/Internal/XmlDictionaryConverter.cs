// <copyright file="XmlDictionaryConverter.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.File.Internal;

using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

/// <summary>
/// 🏷️ Converts between <see cref="XElement"/> and a dictionary shape using the common
/// <c>@attribute</c> / <c>#text</c> / auto-list convention (Q9)~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.5.a.2. Attributes become <c>"@name"</c> keys, element text becomes
/// <c>"#text"</c>, and repeated sibling elements collapse into a list. Round-trips through
/// <see cref="FromDictionary"/>~ 🌸.
/// </remarks>
public static class XmlDictionaryConverter
{
    /// <summary>
    /// Converts an XML element into a dictionary/scalar object graph~ 🏷️.
    /// </summary>
    /// <param name="element">The element to convert.</param>
    /// <returns>A dictionary, string, or <c>null</c>.</returns>
    public static object? ToDictionary(XElement element)
    {
        var hasChildren = element.Elements().Any();
        var hasAttributes = element.Attributes().Any();

        if (!hasChildren && !hasAttributes)
        {
            return element.IsEmpty ? null : element.Value;
        }

        var map = new Dictionary<string, object?>();

        foreach (var attr in element.Attributes())
        {
            map["@" + attr.Name.LocalName] = attr.Value;
        }

        if (!hasChildren)
        {
            var text = element.Nodes().OfType<XText>().Select(t => t.Value).FirstOrDefault();
            if (!string.IsNullOrEmpty(text))
            {
                map["#text"] = text;
            }
        }

        foreach (var group in element.Elements().GroupBy(e => e.Name.LocalName))
        {
            var items = group.Select(ToDictionary).ToList();
            map[group.Key] = items.Count == 1 ? items[0] : items;
        }

        return map;
    }

    /// <summary>
    /// Builds an <see cref="XElement"/> from a dictionary/scalar object graph~ 🏷️.
    /// </summary>
    /// <param name="name">The element name.</param>
    /// <param name="value">The value (dictionary, list, or scalar).</param>
    /// <returns>The constructed element.</returns>
    public static XElement FromDictionary(string name, object? value)
    {
        var element = new XElement(name);

        switch (value)
        {
            case null:
                break;
            case IReadOnlyDictionary<string, object?> map:
                foreach (var kvp in map)
                {
                    if (kvp.Key.StartsWith('@'))
                    {
                        element.SetAttributeValue(kvp.Key[1..], kvp.Value?.ToString() ?? string.Empty);
                    }
                    else if (kvp.Key == "#text")
                    {
                        element.Add(new XText(kvp.Value?.ToString() ?? string.Empty));
                    }
                    else if (kvp.Value is not string && kvp.Value is IEnumerable<object?> list)
                    {
                        foreach (var item in list)
                        {
                            element.Add(FromDictionary(kvp.Key, item));
                        }
                    }
                    else
                    {
                        element.Add(FromDictionary(kvp.Key, kvp.Value));
                    }
                }

                break;
            default:
                element.Value = value.ToString() ?? string.Empty;
                break;
        }

        return element;
    }
}
