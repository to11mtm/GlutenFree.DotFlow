// <copyright file="ReferenceWhitelist.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq.Compilation;

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

/// <summary>
/// 📚 The compilation reference set + the codegen-controlled <c>using</c> allowlist for linq bodies~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// The reference set is the deterministic .NET 8 portable ref assemblies (Basic.Reference.Assemblies,
/// mitigates C8) + linq2db + the resolved plugin assemblies. Because that covers the whole BCL, the
/// real security gate is the <see cref="Usings"/> allowlist (codegen provides ONLY these usings) plus
/// the <see cref="ForbiddenSyntaxWalker"/> — a fully-qualified <c>System.IO.*</c> reach is blocked by
/// the walker, not by trimming references~ 🌸.
/// </para>
/// </remarks>
public static class ReferenceWhitelist
{
    /// <summary>The only <c>using</c> directives codegen prepends to the compilation~ 🧷.</summary>
    public static readonly IReadOnlyList<string> Usings = new[]
    {
        "System",
        "System.Linq",
        "System.Collections.Generic",
        "System.Threading",
        "System.Threading.Tasks",
        "LinqToDB",
    };

    /// <summary>
    /// Builds the metadata reference set: portable BCL refs + linq2db + plugin assemblies~ 📚.
    /// </summary>
    /// <param name="pluginAssemblyLocations">Distinct plugin assembly file locations (for plugin POCOs).</param>
    /// <returns>The metadata references for the compilation.</returns>
    public static IReadOnlyList<MetadataReference> Build(IEnumerable<string> pluginAssemblyLocations)
    {
        var refs = new List<MetadataReference>();

        // 📚 Deterministic .NET 8 BCL reference assemblies (mitigates C8)~
        refs.AddRange(Basic.Reference.Assemblies.Net80.References.All);

        // 🗄️ linq2db (DataConnection, ITable<T>, DataOptions, mapping attributes)~
        refs.Add(MetadataReference.CreateFromFile(typeof(LinqToDB.Data.DataConnection).Assembly.Location));

        // 🧩 Plugin POCO assemblies (trusted, author-registered)~
        foreach (var loc in pluginAssemblyLocations.Where(l => !string.IsNullOrWhiteSpace(l)).Distinct(System.StringComparer.Ordinal))
        {
            refs.Add(MetadataReference.CreateFromFile(loc));
        }

        return refs;
    }
}

