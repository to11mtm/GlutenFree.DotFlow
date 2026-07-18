// <copyright file="IRoslynScriptCompiler.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Roslyn.Abstractions;

using System.Collections.Generic;
using Microsoft.CodeAnalysis;

/// <summary>
/// 🧬 A request to compile a generated script assembly~ ✨.
/// </summary>
/// <param name="AssemblyName">The emitted assembly's name.</param>
/// <param name="GeneratedSource">The full generated C# source (wrapper + user body).</param>
/// <param name="UserBody">The raw user body, scanned standalone by the forbidden-syntax walker.</param>
/// <param name="ExtraReferences">Optional additional metadata references beyond the BCL set.</param>
/// <param name="StrictWarnings">When <c>true</c>, warnings are promoted to errors.</param>
public record ScriptCompileRequest(
    string AssemblyName,
    string GeneratedSource,
    string UserBody,
    IReadOnlyList<MetadataReference>? ExtraReferences = null,
    bool StrictWarnings = false);

/// <summary>
/// 🧬 The result of a compile — bytes on success, diagnostics on failure~ ✨.
/// </summary>
/// <param name="Success">Whether compilation succeeded.</param>
/// <param name="AssemblyBytes">The emitted assembly bytes (non-null on success).</param>
/// <param name="Diagnostics">All diagnostics (errors + warnings).</param>
public record ScriptCompileResult(
    bool Success,
    byte[]? AssemblyBytes,
    IReadOnlyList<ScriptDiagnostic> Diagnostics);

/// <summary>
/// 🧬 Domain-agnostic Roslyn compile pipeline: security-walks the user body, compiles the generated
/// source against a deterministic reference set, and emits assembly bytes~ ✨.
/// </summary>
public interface IRoslynScriptCompiler
{
    /// <summary>
    /// Compiles a script request~ 🧬.
    /// </summary>
    /// <param name="request">The compile request.</param>
    /// <returns>The compile result.</returns>
    ScriptCompileResult Compile(ScriptCompileRequest request);
}
