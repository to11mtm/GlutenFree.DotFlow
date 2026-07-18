// <copyright file="JsonTypeHelpers.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Contracts;

using System;

/// <summary>
/// 🔤 Helpers for turning domain types that don't round-trip cleanly through System.Text.Json
/// (<see cref="Type"/>, <see cref="Version"/>) into serializable strings (Phase 2.7.0 / D6)~ ✨.
/// </summary>
public static class JsonTypeHelpers
{
    /// <summary>
    /// Renders a <see cref="Type"/> as a stable display string (e.g. <c>"System.String"</c>)~ 🔤.
    /// </summary>
    /// <param name="type">The type (may be <c>null</c>).</param>
    /// <returns>The full type name, or <c>null</c>.</returns>
    public static string? TypeName(Type? type) => type?.FullName ?? type?.Name;

    /// <summary>
    /// Renders a <see cref="Version"/> as a string~ 🏷️.
    /// </summary>
    /// <param name="version">The version (may be <c>null</c>).</param>
    /// <returns>The version string, or <c>null</c>.</returns>
    public static string? VersionString(Version? version) => version?.ToString();
}
