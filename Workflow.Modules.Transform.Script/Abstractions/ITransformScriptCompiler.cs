// <copyright file="ITransformScriptCompiler.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Transform.Script.Abstractions;

using System.Collections.Generic;
using Workflow.Scripting.Roslyn.Abstractions;

/// <summary>
/// 🧬 Compiles a transform script body into an assembly via the shared scripting core~ ✨.
/// </summary>
/// <param name="Success">Whether compilation succeeded.</param>
/// <param name="AssemblyBytes">The emitted assembly bytes (non-null on success).</param>
/// <param name="Diagnostics">Compile diagnostics.</param>
public record TransformScriptCompileResult(
    bool Success,
    byte[]? AssemblyBytes,
    IReadOnlyList<ScriptDiagnostic> Diagnostics);

/// <summary>
/// 🧬 Wraps a user transform body into the runtime entry type and compiles it~ ✨.
/// </summary>
public interface ITransformScriptCompiler
{
    /// <summary>The runtime type name of the generated entry point~ 🏷️.</summary>
    string EntryTypeName { get; }

    /// <summary>The static async entry method name~ 🏷️.</summary>
    string EntryMethodName { get; }

    /// <summary>The codegen schema version (part of the cache key)~ 🔢.</summary>
    int SchemaVersion { get; }

    /// <summary>
    /// Compiles a user transform body~ 🧬.
    /// </summary>
    /// <param name="userBody">The user C# body (returns an object over <c>rows</c>/<c>inputs</c>).</param>
    /// <param name="strictWarnings">When <c>true</c>, warnings fail the compile.</param>
    /// <returns>The compile result.</returns>
    TransformScriptCompileResult Compile(string userBody, bool strictWarnings = false);
}
