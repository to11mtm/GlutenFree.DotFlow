// <copyright file="ForbiddenSyntaxWalker.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Roslyn.Compilation;

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Workflow.Scripting.Roslyn.Abstractions;

/// <summary>
/// 🛡️ Syntactic blocklist over a user script body — the shared security walker~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: Defense-in-depth — the user body has no <c>using</c> directives of its own (codegen
/// controls them), so reaching a dangerous API requires a fully-qualified name. This walker rejects
/// the leaf identifiers of those namespaces/types plus <c>unsafe</c>, pointers, <c>stackalloc</c>,
/// and P/Invoke attributes. Generalised from the 2.4.b linq walker (domain-agnostic)~ 🌸.
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

    private readonly List<ScriptDiagnostic> violations = new();

    private ForbiddenSyntaxWalker()
        : base(SyntaxWalkerDepth.Node)
    {
    }

    /// <summary>
    /// Scans a user body for forbidden syntax~ 🔍.
    /// </summary>
    /// <param name="userCodeBody">The raw user method body.</param>
    /// <returns>Error diagnostics for every violation found (empty when clean).</returns>
    public static IReadOnlyList<ScriptDiagnostic> Scan(string userCodeBody)
    {
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
            this.Add("WFSCRIPT100", $"Use of '{node.Identifier.ValueText}' is not allowed in script code~ 🚫");
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
            this.Add("WFSCRIPT101", $"Attribute '{name}' is not allowed in script code~ 🚫");
        }

        base.VisitAttribute(node);
    }

    /// <inheritdoc/>
    public override void VisitUnsafeStatement(UnsafeStatementSyntax node)
    {
        this.Add("WFSCRIPT102", "'unsafe' blocks are not allowed in script code~ 🚫");
        base.VisitUnsafeStatement(node);
    }

    /// <inheritdoc/>
    public override void VisitPointerType(PointerTypeSyntax node)
    {
        this.Add("WFSCRIPT103", "Pointer types are not allowed in script code~ 🚫");
        base.VisitPointerType(node);
    }

    /// <inheritdoc/>
    public override void VisitStackAllocArrayCreationExpression(StackAllocArrayCreationExpressionSyntax node)
    {
        this.Add("WFSCRIPT104", "'stackalloc' is not allowed in script code~ 🚫");
        base.VisitStackAllocArrayCreationExpression(node);
    }

    private void Add(string id, string message)
        => this.violations.Add(new ScriptDiagnostic(id, ScriptDiagnosticSeverity.Error, message));
}
