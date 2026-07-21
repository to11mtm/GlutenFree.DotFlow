// <copyright file="ApiMethodInfo.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Scripts.State;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 💡 Phase 3.4.1 — A single <c>workflow.*</c> API method, described for both Monaco completions/hover
/// and the API reference panel. Framework-free (D2/D4) — no Blazor or JS-interop types~ ✨.
/// </summary>
/// <param name="ClrName">The PascalCase CLR method name (drift-guard key against <c>IWorkflowScriptApi</c>).</param>
/// <param name="JsName">The camelCase name scripts call (from the JS prelude, e.g. <c>getVariable</c>).</param>
/// <param name="Category">The grouping (Variables/Logging/Utilities/Context/HTTP/Files).</param>
/// <param name="ReturnType">A human-readable return type (e.g. <c>object?</c>, <c>Promise</c>).</param>
/// <param name="Summary">A one-line description.</param>
/// <param name="Gated">Whether the call requires a capability (network/file) to be enabled.</param>
/// <param name="Parameters">The declared parameters.</param>
public sealed record ApiMethodInfo(
    string ClrName,
    string JsName,
    string Category,
    string ReturnType,
    string Summary,
    bool Gated,
    IReadOnlyList<ApiParam> Parameters)
{
    /// <summary>Gets a readable signature, e.g. <c>getVariable(name)</c>~ ✍️.</summary>
    public string Signature => $"{this.JsName}({string.Join(", ", this.Parameters.Select(p => p.Name))})";

    /// <summary>Gets a typed signature, e.g. <c>getVariable(name: string): object?</c>~ ✍️.</summary>
    public string TypedSignature
        => $"{this.JsName}({string.Join(", ", this.Parameters.Select(p => $"{p.Name}: {p.Type}"))}): {this.ReturnType}";

    /// <summary>Gets the snippet inserted at the cursor, e.g. <c>workflow.getVariable(name)</c>~ ✍️.</summary>
    public string CallSnippet => $"workflow.{this.Signature}";
}

/// <summary>💡 Phase 3.4.1 — A declared parameter of an <see cref="ApiMethodInfo"/>~ ✨.</summary>
/// <param name="Name">The parameter name.</param>
/// <param name="Type">A human-readable type.</param>
public sealed record ApiParam(string Name, string Type);
