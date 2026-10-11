// <copyright file="ScriptSecurityAnalyzer.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Roslyn.Security;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Workflow.Scripting.Approvals;

/// <summary>
/// 🛡️ §9.2 — finds every sensitive API a user's C# code resolves to, using the compilation's
/// semantic model (so spelling tricks don't matter), plus the old leaf-name blocklist as a
/// pre-filter and P/Invoke attributes. Shared by the script/transform compiler and the LINQ compiler~ ✨.
/// </summary>
public static class ScriptSecurityAnalyzer
{
    /// <summary>
    /// Analyzes the user's code inside a compilation.
    /// </summary>
    /// <param name="compilation">The compilation containing <paramref name="tree"/>.</param>
    /// <param name="tree">The syntax tree holding the generated source.</param>
    /// <param name="userBody">
    /// The user's code exactly as embedded in the tree. Only that region is analyzed, and lines /
    /// columns are reported relative to it. When it can't be located, the whole tree is analyzed.
    /// </param>
    /// <returns>The flags, de-duplicated by symbol and position, in source order.</returns>
    public static IReadOnlyList<SensitiveApiFlag> Analyze(Compilation compilation, SyntaxTree tree, string userBody)
    {
        var text = tree.GetText();
        var start = string.IsNullOrEmpty(userBody) ? -1 : text.ToString().IndexOf(userBody, StringComparison.Ordinal);
        var span = start >= 0 ? new TextSpan(start, userBody.Length) : new TextSpan(0, text.Length);
        var bodyStart = text.Lines.GetLinePosition(span.Start);

        var model = compilation.GetSemanticModel(tree);
        var flags = new List<SensitiveApiFlag>();
        var seen = new HashSet<(string, int, int)>();

        void Add(string symbol, string category, SyntaxNode node)
        {
            var pos = text.Lines.GetLinePosition(node.SpanStart);
            var line = pos.Line - bodyStart.Line + 1;
            var column = pos.Line == bodyStart.Line ? pos.Character - bodyStart.Character + 1 : pos.Character + 1;
            if (seen.Add((category, line, column)))
            {
                flags.Add(SensitiveApiCatalog.Flag(symbol, category, line, column));
            }
        }

        foreach (var node in tree.GetRoot().DescendantNodes(span).Where(n => span.Contains(n.Span)))
        {
            switch (node)
            {
                case AttributeSyntax attribute:
                {
                    var name = attribute.Name.ToString();
                    var leaf = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;
                    if (SensitiveApiCatalog.IsInteropAttribute(leaf))
                    {
                        Add(name, SensitiveApiCatalog.InteropAndMemory, attribute);
                    }

                    break;
                }

                case SimpleNameSyntax name:
                {
                    var symbol = Resolve(model, name);
                    var category = SensitiveApiCatalog.Classify(symbol);
                    if (category is null && symbol is null)
                    {
                        // Didn't bind (compile error, or a name the compilation can't see): fall back to the name list.
                        category = SensitiveApiCatalog.ClassifyIdentifier(name.Identifier.ValueText);
                    }

                    if (category is null && name.Identifier.ValueText == "dynamic"
                        && model.GetTypeInfo(name).Type is { TypeKind: TypeKind.Dynamic })
                    {
                        category = SensitiveApiCatalog.Reflection;
                    }

                    if (category is not null)
                    {
                        Add(symbol?.ToDisplayString() ?? name.Identifier.ValueText, category, name);
                    }

                    break;
                }

                case BaseObjectCreationExpressionSyntax creation:
                {
                    // `new()` (target-typed) has no name node — classify the constructor's type~
                    if (creation is ImplicitObjectCreationExpressionSyntax)
                    {
                        var ctor = model.GetSymbolInfo(creation).Symbol;
                        var category = SensitiveApiCatalog.Classify(ctor);
                        if (category is not null)
                        {
                            Add(ctor!.ToDisplayString(), category, creation);
                        }
                    }

                    break;
                }
            }
        }

        return flags;
    }

    /// <summary>The kind id for plain C# scripts (the Script module / Script Studio).</summary>
    public const string KindScript = "csharp-script";

    /// <summary>The kind id for C# transform scripts.</summary>
    public const string KindTransform = "csharp-transform";

    /// <summary>The kind id for LINQ module bodies.</summary>
    public const string KindLinq = "linq";

    /// <summary>
    /// The approval identity of a piece of code: SHA-256 (hex) of the kind + the code with line endings
    /// normalized and trailing whitespace trimmed. Any other edit changes the hash~ #️⃣.
    /// </summary>
    /// <param name="kind">The code kind.</param>
    /// <param name="code">The exact analyzed code.</param>
    /// <returns>The lowercase hex hash.</returns>
    public static string CodeHash(string kind, string code)
    {
        var normalized = (code ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(kind + "\n" + normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// The diagnostic message for a flag that blocks compilation until approved.
    /// </summary>
    /// <param name="flag">The flag.</param>
    /// <param name="codeHash">The code hash to request approval for.</param>
    /// <returns>The message.</returns>
    public static string DeniedMessage(SensitiveApiFlag flag, string codeHash)
        => $"'{flag.Symbol}' needs reviewer approval ({flag.Title}): {flag.Danger} " +
           $"Request approval for code hash {codeHash}.";

    private static ISymbol? Resolve(SemanticModel model, SimpleNameSyntax name)
    {
        var info = model.GetSymbolInfo(name);
        return info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
    }
}
