// <copyright file="FileModuleSupport.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.File.Internal;

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🧰 Shared helpers for file-system modules — property readers + validate-then-resolve~ 📁✨.
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.5.a.0. Mirrors the DRY <c>DbModuleSupport</c> from 2.4.a.2 —
/// keeps the boring config/property/validation plumbing in one place~ 🌸.
/// </remarks>
public static class FileModuleSupport
{
    /// <summary>
    /// Reads a string property (trimmed), or <c>null</c> if missing/empty~ 🔤.
    /// </summary>
    /// <param name="props">The property bag.</param>
    /// <param name="key">The property key.</param>
    /// <returns>The string value or <c>null</c>.</returns>
    public static string? GetString(IReadOnlyDictionary<string, object?> props, string key)
    {
        if (!props.TryGetValue(key, out var val) || val is null)
        {
            return null;
        }

        var s = val as string ?? val.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    /// <summary>
    /// Reads a bool property (accepts bool or parseable string)~ ✅.
    /// </summary>
    /// <param name="props">The property bag.</param>
    /// <param name="key">The property key.</param>
    /// <param name="defaultValue">Value returned when missing/unparseable.</param>
    /// <returns>The bool value.</returns>
    public static bool GetBool(IReadOnlyDictionary<string, object?> props, string key, bool defaultValue)
    {
        if (!props.TryGetValue(key, out var val) || val is null)
        {
            return defaultValue;
        }

        return val switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            _ => defaultValue,
        };
    }

    /// <summary>
    /// Reads a long property (accepts long/int/double/parseable string)~ 🔢.
    /// </summary>
    /// <param name="props">The property bag.</param>
    /// <param name="key">The property key.</param>
    /// <returns>The long value or <c>null</c>.</returns>
    public static long? TryGetLong(IReadOnlyDictionary<string, object?> props, string key)
    {
        if (!props.TryGetValue(key, out var val) || val is null)
        {
            return null;
        }

        return val switch
        {
            long l => l,
            int i => i,
            double d => (long)d,
            string s when long.TryParse(s, out var parsed) => parsed,
            _ => null,
        };
    }

    /// <summary>
    /// Reads an int property (accepts int/long/double/parseable string)~ 🔢.
    /// </summary>
    /// <param name="props">The property bag.</param>
    /// <param name="key">The property key.</param>
    /// <returns>The int value or <c>null</c>.</returns>
    public static int? TryGetInt(IReadOnlyDictionary<string, object?> props, string key)
    {
        var l = TryGetLong(props, key);
        return l.HasValue ? (int)l.Value : null;
    }

    /// <summary>
    /// Resolves the <see cref="IWorkflowPathValidator"/> and validates a raw path~ 🛡️.
    /// </summary>
    /// <param name="context">The module execution context.</param>
    /// <param name="rawPath">The raw path to validate.</param>
    /// <param name="intent">The access intent.</param>
    /// <param name="resolvedPath">The resolved absolute path when valid.</param>
    /// <param name="failure">A ready-to-return failure result when invalid.</param>
    /// <returns><c>true</c> when the path is valid; otherwise <c>false</c>.</returns>
    public static bool TryValidatePath(
        ModuleExecutionContext context,
        string rawPath,
        PathAccessIntent intent,
        out string resolvedPath,
        out ModuleResult? failure)
    {
        resolvedPath = string.Empty;
        failure = null;

        var validator = context.Services.GetService<IWorkflowPathValidator>();
        if (validator is null)
        {
            failure = ModuleResult.Fail(
                "🛡️ IWorkflowPathValidator is not registered — call AddFileSystemModules() at host startup~ 💔");
            return false;
        }

        var result = validator.ValidatePath(rawPath, intent);
        if (!result.IsValid || result.ResolvedPath is null)
        {
            failure = ModuleResult.Fail(
                $"🛡️ Path rejected: {result.Reason}~ 🚫",
                new PathSecurityException(rawPath, result.Reason ?? "invalid path"));
            return false;
        }

        resolvedPath = result.ResolvedPath;
        return true;
    }

    /// <summary>
    /// Resolves the effective <see cref="FileSystemModuleOptions"/> from DI (falling back to
    /// defaults when unregistered)~ ⚙️.
    /// </summary>
    /// <param name="services">The service provider.</param>
    /// <returns>The resolved options.</returns>
    public static FileSystemModuleOptions GetFileSystemOptions(this IServiceProvider services)
        => services.GetService<IOptions<FileSystemModuleOptions>>()?.Value ?? new FileSystemModuleOptions();
}
