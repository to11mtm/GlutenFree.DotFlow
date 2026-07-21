// <copyright file="LinqResultMaterializer.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq.Execution;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 💥 Thrown when a linq body returns a non-materialised result (e.g. <c>IQueryable</c>) that would
/// pin the collectible ALC or leak an ALC-rooted reference (§8.4 / D8)~ 🚫.
/// </summary>
public sealed class LinqMaterializationException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="LinqMaterializationException"/> class~.</summary>
    /// <param name="message">The message.</param>
    public LinqMaterializationException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// 📤 The materialised (ALC-free) outcome of a linq body~ ✨.
/// </summary>
public sealed class LinqExecutionResult
{
    /// <summary>Gets the materialised rows (POCO sequence → column→value dicts), or <c>null</c> for scalars.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>>? Rows { get; init; }

    /// <summary>Gets the raw materialised value (rows, a scalar list, or a single scalar).</summary>
    public object? Result { get; init; }

    /// <summary>Gets the row/element count when the result is a sequence; otherwise <c>null</c>.</summary>
    public int? RowCount { get; init; }

    /// <summary>Gets a weak reference to the collectible ALC (for unload assertions in tests).</summary>
    public WeakReference? AlcWeakRef { get; init; }
}

/// <summary>
/// 📤 Copies a linq body's result out of the collectible ALC into pure BCL types (mitigates §8.4)~ 💖.
/// </summary>
public static class LinqResultMaterializer
{
    /// <summary>
    /// Materialises a raw result into rows/scalars, rejecting non-materialised (<c>IQueryable</c>) shapes~ 🎯.
    /// </summary>
    /// <param name="raw">The raw value returned by the compiled body.</param>
    /// <returns>The materialised rows/scalar + count.</returns>
    /// <exception cref="LinqMaterializationException">When the result is a lazy <c>IQueryable</c>.</exception>
    public static LinqExecutionResult Materialize(object? raw)
    {
        if (raw is null)
        {
            return new LinqExecutionResult { Result = null };
        }

        if (raw is IQueryable)
        {
            throw new LinqMaterializationException(
                "Linq body returned an IQueryable — materialise it (e.g. .ToList()) before returning so no "
                + "ALC-rooted reference escapes~ 🚫");
        }

        if (IsScalar(raw.GetType()))
        {
            return new LinqExecutionResult { Result = raw };
        }

        if (raw is IEnumerable enumerable)
        {
            var rows = new List<IReadOnlyDictionary<string, object?>>();
            var scalars = new List<object?>();
            var anyPoco = false;

            foreach (var item in enumerable)
            {
                if (item is not null && !IsScalar(item.GetType()))
                {
                    rows.Add(ToDictionary(item));
                    anyPoco = true;
                }
                else
                {
                    scalars.Add(item);
                }
            }

            if (anyPoco)
            {
                return new LinqExecutionResult { Rows = rows, Result = rows, RowCount = rows.Count };
            }

            return new LinqExecutionResult { Result = scalars, RowCount = scalars.Count };
        }

        // Single non-scalar object → reflect it into one row.
        var single = ToDictionary(raw);
        return new LinqExecutionResult { Rows = new[] { single }, Result = single, RowCount = 1 };
    }

    private static IReadOnlyDictionary<string, object?> ToDictionary(object item)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in item.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (prop.CanRead && prop.GetIndexParameters().Length == 0)
            {
                dict[prop.Name] = prop.GetValue(item);
            }
        }

        return dict;
    }

    private static bool IsScalar(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t.IsPrimitive
            || t.IsEnum
            || t == typeof(string)
            || t == typeof(decimal)
            || t == typeof(Guid)
            || t == typeof(DateTime)
            || t == typeof(DateTimeOffset)
            || t == typeof(TimeSpan)
            || t == typeof(byte[]);
    }
}

