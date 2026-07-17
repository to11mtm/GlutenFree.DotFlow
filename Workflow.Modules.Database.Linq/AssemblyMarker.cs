// <copyright file="AssemblyMarker.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq;

using System.Linq;
using Basic.Reference.Assemblies;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>
/// 🧬 Phase 2.4.b.0 — Assembly marker + Roslyn-toolchain smoke for the typed linq family~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// This type exists so the scaffolding slice (2.4.b.0) has verifiable substance before the real
/// compiler/previewer/module land in 2.4.b.1/2.4.b.3/2.4.b.4. It touches a Roslyn type
/// (<see cref="CSharpSyntaxTree"/>) and the portable reference set (<see cref="ReferenceAssemblies"/>)
/// so the C# compiler actually emits assembly references to <c>Microsoft.CodeAnalysis.*</c> and
/// <c>Basic.Reference.Assemblies</c> — which the quarantine test then asserts live ONLY in this
/// assembly and never leak into <c>Workflow.Modules</c> (D14)~ 🌸.
/// </para>
/// </remarks>
public static class AssemblyMarker
{
    /// <summary>
    /// Gets the simple name of the portable-reference-assemblies assembly (proves the
    /// <c>Basic.Reference.Assemblies</c> package is referenced by this assembly)~ 📚.
    /// </summary>
    public static string ReferenceAssembliesAssemblyName =>
        typeof(ReferenceAssemblies).Assembly.GetName().Name ?? string.Empty;

    /// <summary>
    /// Parses a trivial compilation unit to prove the Roslyn C# toolchain resolves + runs~ 🧠.
    /// </summary>
    /// <returns>The number of syntax nodes in <c>class C { }</c> (a non-zero smoke value).</returns>
    public static int RoslynToolchainSmoke()
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText("class C { }");
        return tree.GetRoot().DescendantNodesAndSelf().Count();
    }
}



