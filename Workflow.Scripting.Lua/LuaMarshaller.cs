// <copyright file="LuaMarshaller.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Lua;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MoonSharp.Interpreter;

/// <summary>
/// 🌙 Phase 3.1.3 — Marshals values between .NET (dictionaries/lists/primitives) and MoonSharp
/// <see cref="DynValue"/> tables, with a depth cap to bound recursion~ ✨.
/// </summary>
internal static class LuaMarshaller
{
    private const int MaxDepth = 32;

    public static DynValue ToLua(Script script, object? value, int depth = 0)
    {
        if (depth > MaxDepth)
        {
            throw new InvalidOperationException($"Value nesting exceeds the maximum depth of {MaxDepth}.");
        }

        switch (value)
        {
            case null:
                return DynValue.Nil;
            case bool b:
                return DynValue.NewBoolean(b);
            case string s:
                return DynValue.NewString(s);
            case int or long or short or byte:
                return DynValue.NewNumber(Convert.ToDouble(value, CultureInfo.InvariantCulture));
            case float or double or decimal:
                return DynValue.NewNumber(Convert.ToDouble(value, CultureInfo.InvariantCulture));
            case IReadOnlyDictionary<string, object?> dict:
                var t = new Table(script);
                foreach (var (k, v) in dict)
                {
                    t[k] = ToLua(script, v, depth + 1);
                }

                return DynValue.NewTable(t);
            case System.Collections.IEnumerable enumerable:
                var arr = new Table(script);
                var i = 1;
                foreach (var item in enumerable)
                {
                    arr[i++] = ToLua(script, item, depth + 1);
                }

                return DynValue.NewTable(arr);
            default:
                return DynValue.NewString(value.ToString());
        }
    }

    public static object? FromLua(DynValue value, int depth = 0)
    {
        if (depth > MaxDepth)
        {
            throw new InvalidOperationException($"Result nesting exceeds the maximum depth of {MaxDepth}.");
        }

        switch (value.Type)
        {
            case DataType.Nil:
            case DataType.Void:
                return null;
            case DataType.Boolean:
                return value.Boolean;
            case DataType.Number:
                var d = value.Number;
                if (d == Math.Floor(d) && d >= long.MinValue && d <= long.MaxValue)
                {
                    var l = (long)d;
                    return l is >= int.MinValue and <= int.MaxValue ? (int)l : l;
                }

                return d;
            case DataType.String:
                return value.String;
            case DataType.Table:
                return TableToClr(value.Table, depth);
            default:
                return value.ToPrintString();
        }
    }

    private static object? TableToClr(Table table, int depth)
    {
        // A table is an "array" when its keys are 1..N contiguous integers~
        var length = table.Length;
        var keys = table.Keys.ToList();
        var isArray = length > 0 && keys.Count == length && keys.All(k => k.Type == DataType.Number);

        if (isArray)
        {
            var list = new List<object?>(length);
            for (var i = 1; i <= length; i++)
            {
                list.Add(FromLua(table.Get(i), depth + 1));
            }

            return list;
        }

        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in table.Pairs)
        {
            var key = pair.Key.Type == DataType.String ? pair.Key.String : pair.Key.ToPrintString();
            dict[key] = FromLua(pair.Value, depth + 1);
        }

        return dict;
    }
}
