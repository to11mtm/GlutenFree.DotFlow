// <copyright file="ForbiddenSyntaxWalker.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq.Compilation;

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Workflow.Modules.Database.Linq.Abstractions;

/// <summary>
/// 🛡️ Syntactic blocklist over the user's linq body (mitigates C1)~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// Defense-in-depth: the user body has no <c>using</c> directives of its own (codegen controls
/// them — see <see cref="ReferenceWhitelist"/>), so the only way to reach a dangerous API is a
/// fully-qualified name (e.g. <c>System.IO.File.Delete</c>). This walker rejects the leaf identifiers
/// of those namespaces/types (<c>File</c>, <c>Process</c>, …), plus <c>unsafe</c>, pointers,
/// <c>stackalloc</c>, and P/Invoke attributes~ 🌸.
/// </para>
/// <para>
/// CopilotNote: We walk ONLY the user body (parsed standalone) so codegen'd identifiers + column
/// names can't cause false positives. Reserved words are documented for authors~.
/// </para>
/// </remarks>
public sealed class ForbiddenSyntaxWalker : CSharpSyntaxWalker
{
    private static readonly HashSet<string> BannedIdentifiers = new(System.StringComparer.Ordinal)
    {
        // process / environment / app host
        "Process", "ProcessStartInfo", "AppDomain", "AppContext", "Environment",
        // file system
        "File", "Directory", "FileInfo", "DirectoryInfo", "FileStream", "Path", "DriveInfo",
        // network
        "Socket", "TcpClient", "TcpListener", "UdpClient", "HttpClient", "WebClient",
        "WebRequest", "HttpWebRequest", "Dns",
        // reflection / interop / memory
        "Activator", "Assembly", "AssemblyLoadContext", "Marshal", "NativeMemory",
        "MemoryMarshal", "RuntimeHelpers", "GCHandle", "Unsafe", "ILGenerator",
        // registry / misc
        "Registry", "RegistryKey",
    };

    private static readonly HashSet<string> BannedAttributes = new(System.StringComparer.Ordinal)
    {
        "DllImport", "DllImportAttribute", "UnmanagedCallersOnly", "UnmanagedCallersOnlyAttribute",
    };

    private readonly List<LinqDiagnostic> violations = new();

    private ForbiddenSyntaxWalker()
        : base(SyntaxWalkerDepth.Node)
    {
    }

    /// <summary>
    /// Scans a user body for forbidden syntax~ 🔍.
    /// </summary>
    /// <param name="userCodeBody">The raw user method body.</param>
    /// <returns>Error diagnostics for every violation found (empty when clean).</returns>
    public static IReadOnlyList<LinqDiagnostic> Scan(string userCodeBody)
    {
        // Wrap the body in a throwaway method so it parses as statements.
        var source = "class __Probe { void __M() {\n" + userCodeBody + "\n} }";
        var tree = CSharpSyntaxTree.ParseText(source);
        var walker = new ForbiddenSyntaxWalker();
        walker.Visit(tree.GetRoot());
        return walker.violations;
    }

    /// <inheritdoc/>
    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (BannedIdentifiers.Contains(node.Identifier.ValueText))
        {
            this.Add("WFLINQ100", $"Use of '{node.Identifier.ValueText}' is not allowed in linq module code~ 🚫");
        }

        base.VisitIdentifierName(node);
    }

    /// <inheritdoc/>
    public override void VisitAttribute(AttributeSyntax node)
    {
        var name = node.Name.ToString();
        var leaf = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;
        if (BannedAttributes.Contains(leaf))
        {
            this.Add("WFLINQ101", $"Attribute '{name}' is not allowed in linq module code~ 🚫");
        }

        base.VisitAttribute(node);
    }

    /// <inheritdoc/>
    public override void VisitUnsafeStatement(UnsafeStatementSyntax node)
    {
        this.Add("WFLINQ102", "'unsafe' blocks are not allowed in linq module code~ 🚫");
        base.VisitUnsafeStatement(node);
    }

    /// <inheritdoc/>
    public override void VisitPointerType(PointerTypeSyntax node)
    {
        this.Add("WFLINQ103", "Pointer types are not allowed in linq module code~ 🚫");
        base.VisitPointerType(node);
    }

    /// <inheritdoc/>
    public override void VisitStackAllocArrayCreationExpression(StackAllocArrayCreationExpressionSyntax node)
    {
        this.Add("WFLINQ104", "'stackalloc' is not allowed in linq module code~ 🚫");
        base.VisitStackAllocArrayCreationExpression(node);
    }

    private void Add(string id, string message)
        => this.violations.Add(new LinqDiagnostic(id, LinqDiagnosticSeverity.Error, message));
}

