// <copyright file="SampleDataGenerator.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq.Preview;

using System;

/// <summary>
/// 🎲 Deterministic sample values per POCO property type for the preview sandbox (2.4.b.4)~ ✨.
/// </summary>
internal static class SampleDataGenerator
{
    private static readonly DateTime BaseUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Produces a deterministic sample value for a property type + row index~ 🎯.
    /// </summary>
    /// <param name="type">The property CLR type (nullable allowed).</param>
    /// <param name="rowIndex">The 0-based sample row index.</param>
    /// <returns>A sample value (or <c>null</c> for unsupported types).</returns>
    public static object? For(Type type, int rowIndex)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;

        if (t == typeof(string))
        {
            return $"sample-{rowIndex}";
        }

        if (t == typeof(int))
        {
            return rowIndex + 1;
        }

        if (t == typeof(long))
        {
            return (long)(rowIndex + 1);
        }

        if (t == typeof(short))
        {
            return (short)(rowIndex + 1);
        }

        if (t == typeof(decimal))
        {
            return (rowIndex + 1) + 0.5m;
        }

        if (t == typeof(double))
        {
            return (rowIndex + 1) + 0.5d;
        }

        if (t == typeof(float))
        {
            return (rowIndex + 1) + 0.5f;
        }

        if (t == typeof(bool))
        {
            return rowIndex % 2 == 0;
        }

        if (t == typeof(Guid))
        {
            return DeterministicGuid(rowIndex);
        }

        if (t == typeof(DateTime))
        {
            return BaseUtc.AddDays(rowIndex);
        }

        if (t == typeof(DateTimeOffset))
        {
            return new DateTimeOffset(BaseUtc.AddDays(rowIndex));
        }

        if (t == typeof(TimeSpan))
        {
            return TimeSpan.FromHours(rowIndex + 1);
        }

        if (t == typeof(byte[]))
        {
            return new byte[] { (byte)(rowIndex + 1) };
        }

        return null;
    }

    private static Guid DeterministicGuid(int rowIndex)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(rowIndex + 1).CopyTo(bytes, 0);
        return new Guid(bytes);
    }
}

