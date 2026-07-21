// <copyright file="NodeIdGenerator.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System.Collections.Generic;

/// <summary>
/// 🏷️ Phase 3.3.a.2 — Generates short, human-friendly, unique node ids from a module id:
/// <c>builtin.http.request</c> → <c>request-1</c>, <c>request-2</c>, …~ ✨.
/// </summary>
public static class NodeIdGenerator
{
    /// <summary>Generates a unique node id for a module, avoiding the existing ids~ 🏷️.</summary>
    /// <param name="moduleId">The module id.</param>
    /// <param name="existingIds">The ids already in use.</param>
    /// <returns>A unique, stable id.</returns>
    public static string Generate(string moduleId, ISet<string> existingIds)
    {
        var stem = Stem(moduleId);
        var n = 1;
        string candidate;
        do
        {
            candidate = $"{stem}-{n}";
            n++;
        }
        while (existingIds.Contains(candidate));

        return candidate;
    }

    private static string Stem(string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            return "node";
        }

        var lastDot = moduleId.LastIndexOf('.');
        var stem = lastDot >= 0 && lastDot < moduleId.Length - 1
            ? moduleId[(lastDot + 1)..]
            : moduleId;

        // Keep it identifier-friendly.
        var cleaned = new System.Text.StringBuilder();
        foreach (var ch in stem)
        {
            cleaned.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-');
        }

        var result = cleaned.ToString().Trim('-');
        return string.IsNullOrEmpty(result) ? "node" : result;
    }
}
