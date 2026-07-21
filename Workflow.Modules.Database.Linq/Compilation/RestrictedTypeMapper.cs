// <copyright file="RestrictedTypeMapper.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq.Compilation;

using System;
using System.Collections.Generic;

/// <summary>
/// 🔒 Restricted <see cref="Type"/> → C# type-name mapping for <c>LinqInputs</c> codegen (design doc §8.6 Phase 1)~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: Intentionally conservative — scalars only. Collections/LanguageExt generics are
/// Phase 2 (2.4.b.P1). Anything not in the map falls back to <c>object?</c> (the escape hatch, no
/// Roslyn benefit) — the caller decides warn-vs-error via strict mode~ 🌸.
/// </remarks>
public static class RestrictedTypeMapper
{
    private static readonly Dictionary<Type, string> Scalars = new()
    {
        [typeof(string)] = "string",
        [typeof(int)] = "int",
        [typeof(long)] = "long",
        [typeof(double)] = "double",
        [typeof(decimal)] = "decimal",
        [typeof(bool)] = "bool",
        [typeof(Guid)] = "global::System.Guid",
        [typeof(DateTime)] = "global::System.DateTime",
        [typeof(DateTimeOffset)] = "global::System.DateTimeOffset",
        [typeof(TimeSpan)] = "global::System.TimeSpan",
    };

    /// <summary>
    /// Tries to map a runtime type to an allowlisted C# type name~ 🎯.
    /// </summary>
    /// <param name="type">The property's runtime <see cref="Type"/>.</param>
    /// <param name="csharpName">The emitted C# type name (or <c>object?</c> when not allowlisted).</param>
    /// <returns><c>true</c> when the type is allowlisted (typed); <c>false</c> when it fell back to <c>object?</c>.</returns>
    public static bool TryMap(Type? type, out string csharpName)
    {
        if (type is null)
        {
            csharpName = "object?";
            return false;
        }

        // object / object? are "allowed" but carry no Roslyn benefit (escape hatch)~
        if (type == typeof(object))
        {
            csharpName = "object?";
            return true;
        }

        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            if (Scalars.TryGetValue(underlying, out var baseName) && underlying.IsValueType)
            {
                csharpName = baseName + "?";
                return true;
            }

            csharpName = "object?";
            return false;
        }

        if (Scalars.TryGetValue(type, out var name))
        {
            csharpName = name;
            return true;
        }

        csharpName = "object?";
        return false;
    }
}

