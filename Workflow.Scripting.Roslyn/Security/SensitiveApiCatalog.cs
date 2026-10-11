// <copyright file="SensitiveApiCatalog.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Roslyn.Security;

using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Workflow.Scripting.Approvals;

/// <summary>
/// 🚩 §9.2 — which resolved APIs need reviewer approval, grouped into categories, each with a
/// plain-language explanation of the danger (shown to authors and reviewers in the UI)~ ✨.
/// </summary>
/// <remarks>
/// Classification is by the symbol the code BINDS to (namespace / type / member), not by how it's
/// spelled, so a fully-qualified name, an alias or a chained call all land in the same category.
/// This is a denylist of capabilities; it is paired with reviewer approval of the exact code, so a
/// miss here is a defence-in-depth gap, not the only line of defence.
/// </remarks>
public static class SensitiveApiCatalog
{
    /// <summary>Reflection / late binding: can reach any type and member by name.</summary>
    public const string Reflection = "Reflection";

    /// <summary>File system access.</summary>
    public const string FileSystem = "FileSystem";

    /// <summary>Network access.</summary>
    public const string Network = "Network";

    /// <summary>Starting processes.</summary>
    public const string Process = "Process";

    /// <summary>Environment, app domain and registry access.</summary>
    public const string Environment = "Environment";

    /// <summary>Native interop and raw memory.</summary>
    public const string InteropAndMemory = "InteropAndMemory";

    /// <summary>Loading assemblies.</summary>
    public const string AssemblyLoading = "AssemblyLoading";

    /// <summary>Generating and running code at runtime.</summary>
    public const string DynamicCode = "DynamicCode";

    private static readonly Dictionary<string, (string Title, string Danger)> Categories = new(StringComparer.Ordinal)
    {
        [Reflection] = (
            "Reflection / late binding",
            "Reflection can look up any type and call any method in the server process by name — including file, network and process APIs — so it gets around every other check. Code using it can do anything the server itself can do."),
        [FileSystem] = (
            "File system access",
            "Can read, write or delete any file the server process can reach: configuration and secrets, other workflows' data, logs, or the application's own files."),
        [Network] = (
            "Network access",
            "Can connect to any host the server can reach, including internal services and cloud metadata endpoints (server-side request forgery), and send data out of your network."),
        [Process] = (
            "Starting processes",
            "Can start other programs on the server, which amounts to running arbitrary commands with the server's permissions."),
        [Environment] = (
            "Environment and host settings",
            "Can read environment variables and host settings (which often hold secrets and connection strings), change them, or exit the server process."),
        [InteropAndMemory] = (
            "Native interop / raw memory",
            "Steps outside .NET memory safety: native calls and raw memory access can read secrets from memory, crash the server, or run arbitrary native code."),
        [AssemblyLoading] = (
            "Loading assemblies",
            "Can load and run arbitrary compiled code from disk or from bytes, which this review never sees."),
        [DynamicCode] = (
            "Generating code at runtime",
            "Builds and runs new code while the script is running, so what actually executes is not the code being reviewed."),
    };

    private static readonly HashSet<string> SafeIoTypes = new(StringComparer.Ordinal)
    {
        "Stream", "MemoryStream", "BufferedStream", "StringReader", "StringWriter", "TextReader", "TextWriter",
        "BinaryReader", "BinaryWriter", "SeekOrigin", "IOException", "EndOfStreamException", "InvalidDataException",
    };

    private static readonly HashSet<string> TypeReflectionMembers = new(StringComparer.Ordinal)
    {
        "GetTypeFromHandle", "GetTypeFromProgID", "GetTypeFromCLSID", "ReflectionOnlyGetType",
        "GetMethod", "GetMethods", "GetProperty", "GetProperties", "GetField", "GetFields",
        "GetMember", "GetMembers", "GetConstructor", "GetConstructors", "GetEvent", "GetEvents",
        "GetNestedType", "GetNestedTypes", "GetInterface", "GetInterfaceMap", "GetDefaultMembers",
        "FindMembers", "FindInterfaces", "InvokeMember", "MakeGenericType", "MakeGenericMethodParameter",
        "Assembly", "Module", "TypeHandle", "TypeInitializer", "DeclaringMethod",
    };

    /// <summary>Leaf identifiers flagged even before binding (the old name blocklist, now a pre-filter).</summary>
    private static readonly Dictionary<string, string> IdentifierPrefilter = new(StringComparer.Ordinal)
    {
        ["Process"] = Process, ["ProcessStartInfo"] = Process,
        ["AppDomain"] = Environment, ["AppContext"] = Environment, ["Environment"] = Environment,
        ["Registry"] = Environment, ["RegistryKey"] = Environment,
        ["File"] = FileSystem, ["Directory"] = FileSystem, ["FileInfo"] = FileSystem, ["DirectoryInfo"] = FileSystem,
        ["FileStream"] = FileSystem, ["Path"] = FileSystem, ["DriveInfo"] = FileSystem,
        ["Socket"] = Network, ["TcpClient"] = Network, ["TcpListener"] = Network, ["UdpClient"] = Network,
        ["HttpClient"] = Network, ["WebClient"] = Network, ["WebRequest"] = Network, ["HttpWebRequest"] = Network, ["Dns"] = Network,
        ["Activator"] = Reflection, ["Assembly"] = Reflection, ["MethodInfo"] = Reflection, ["InvokeMember"] = Reflection,
        ["AssemblyLoadContext"] = AssemblyLoading,
        ["Marshal"] = InteropAndMemory, ["NativeMemory"] = InteropAndMemory, ["MemoryMarshal"] = InteropAndMemory,
        ["RuntimeHelpers"] = InteropAndMemory, ["GCHandle"] = InteropAndMemory, ["Unsafe"] = InteropAndMemory,
        ["ILGenerator"] = DynamicCode,
    };

    private static readonly HashSet<string> InteropAttributes = new(StringComparer.Ordinal)
    {
        "DllImport", "DllImportAttribute", "LibraryImport", "LibraryImportAttribute",
        "UnmanagedCallersOnly", "UnmanagedCallersOnlyAttribute",
    };

    /// <summary>Gets every category id with its title and danger text (for API/UI legends).</summary>
    public static IReadOnlyDictionary<string, (string Title, string Danger)> All => Categories;

    /// <summary>Builds a flag for a category.</summary>
    /// <param name="symbol">The symbol display string.</param>
    /// <param name="category">The category id.</param>
    /// <param name="line">1-based line.</param>
    /// <param name="column">1-based column.</param>
    /// <returns>The flag with title and danger filled in.</returns>
    public static SensitiveApiFlag Flag(string symbol, string category, int? line, int? column)
    {
        var (title, danger) = Categories[category];
        return new SensitiveApiFlag(symbol, category, title, danger, line, column);
    }

    /// <summary>The category of a leaf identifier from the name pre-filter, if any.</summary>
    /// <param name="identifier">The identifier text.</param>
    /// <returns>The category, or null.</returns>
    public static string? ClassifyIdentifier(string identifier)
        => IdentifierPrefilter.TryGetValue(identifier, out var category) ? category : null;

    /// <summary>Whether an attribute name (leaf) is a native-interop attribute.</summary>
    /// <param name="leafName">The attribute's leaf name.</param>
    /// <returns><c>true</c> for P/Invoke-style attributes.</returns>
    public static bool IsInteropAttribute(string leafName) => InteropAttributes.Contains(leafName);

    /// <summary>The category of a bound symbol, or null when it isn't sensitive.</summary>
    /// <param name="symbol">The symbol the code binds to.</param>
    /// <returns>The category id, or null.</returns>
    public static string? Classify(ISymbol? symbol)
    {
        switch (symbol)
        {
            case null:
            case INamespaceSymbol:
            case ILocalSymbol:
            case IParameterSymbol:
            case ILabelSymbol:
            case IRangeVariableSymbol:
                return null;
            case IDynamicTypeSymbol:
                return Reflection;
            case IMethodSymbol { ReducedFrom: { } reduced }:
                symbol = reduced;
                break;
        }

        var type = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        if (type is null)
        {
            return null;
        }

        // Nested types inherit their outermost type's namespace~
        var ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        var typeName = type.OriginalDefinition.Name;
        var member = symbol is INamedTypeSymbol ? null : symbol.Name;

        if (StartsWith(ns, "System.Reflection.Emit"))
        {
            return DynamicCode;
        }

        if (StartsWith(ns, "System.Runtime.Loader"))
        {
            return AssemblyLoading;
        }

        if (ns == "System.Reflection" && typeName == "Assembly" && member is not null
            && (member.StartsWith("Load", StringComparison.Ordinal) || member == "UnsafeLoadFrom"))
        {
            return AssemblyLoading;
        }

        if (StartsWith(ns, "System.Reflection"))
        {
            return Reflection;
        }

        if (ns == "System")
        {
            switch (typeName)
            {
                case "Type" when member is not null
                    && (TypeReflectionMembers.Contains(member)
                        || (member == "GetType" && symbol is IMethodSymbol { Parameters.Length: > 0 })):
                case "Activator":
                case "Delegate" when member is "CreateDelegate" or "DynamicInvoke" or "Method":
                case "MulticastDelegate" when member is "DynamicInvoke" or "Method":
                    return Reflection;
                case "Environment":
                case "AppDomain":
                case "AppContext":
                    return Environment;
                case "Buffer" when member is "MemoryCopy":
                    return InteropAndMemory;
            }

            return null;
        }

        if (ns == "System.Linq.Expressions" && member is "Compile" or "CompileToMethod")
        {
            return DynamicCode;
        }

        if (ns == "System.IO.Compression")
        {
            return typeName.StartsWith("ZipFile", StringComparison.Ordinal) ? FileSystem : null;
        }

        if (StartsWith(ns, "System.IO"))
        {
            return SafeIoTypes.Contains(typeName) ? null : FileSystem;
        }

        if (StartsWith(ns, "System.Net"))
        {
            return Network;
        }

        // XmlDocument.Load / XDocument.Load / XmlReader.Create / *.Save take a path or URL
        // (LoadXml / Parse take a string and are fine)~
        if (StartsWith(ns, "System.Xml") && member is "Load" or "Save" or "Create")
        {
            return FileSystem;
        }

        if (ns == "System.Diagnostics" && typeName.StartsWith("Process", StringComparison.Ordinal))
        {
            return Process;
        }

        if (StartsWith(ns, "Microsoft.Win32"))
        {
            return Environment;
        }

        if (StartsWith(ns, "System.Runtime.InteropServices")
            || (ns == "System.Runtime.CompilerServices" && typeName is "Unsafe" or "RuntimeHelpers"))
        {
            return InteropAndMemory;
        }

        return null;
    }

    private static bool StartsWith(string ns, string prefix)
        => ns == prefix || ns.StartsWith(prefix + ".", StringComparison.Ordinal);
}
