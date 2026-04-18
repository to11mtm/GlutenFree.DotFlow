// <copyright file="PluginAssemblyLoadContext.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Loading;

using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

/// <summary>
/// 🔌 A collectible <see cref="AssemblyLoadContext"/> for loading plugin assemblies
/// in isolation while sharing host-framework assemblies to preserve type identity~ ✨
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: The "shared types" problem: if we load Workflow.Modules.dll AGAIN in
/// the plugin ALC, the host's <c>IWorkflowModule</c> and the plugin's <c>IWorkflowModule</c>
/// are DIFFERENT types — even though the code is identical. The cast to IWorkflowModule
/// would then silently fail at runtime. To prevent this, we override <see cref="Load"/>
/// to redirect "known host" assemblies back to the default/host context~ 💖
/// </para>
/// <para>
/// Which assemblies are shared:
/// <list type="bullet">
/// <item>All assemblies already loaded in the host <see cref="AssemblyLoadContext.Default"/></item>
/// </list>
/// This is the simplest correct policy — it means the plugin DLL itself is isolated
/// (loaded in this ALC) but everything it depends on that the host already has is shared.
/// </para>
/// </remarks>
internal sealed class PluginAssemblyLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _pluginPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginAssemblyLoadContext"/> class.
    /// </summary>
    /// <param name="pluginPath">The full path to the plugin assembly DLL.</param>
    public PluginAssemblyLoadContext(string pluginPath)
        : base(name: $"Plugin:{Path.GetFileNameWithoutExtension(pluginPath)}", isCollectible: true)
    {
        _pluginPath = pluginPath;
        // CopilotNote: AssemblyDependencyResolver reads the .deps.json and .runtimeconfig.json
        // next to the plugin DLL to find its dependencies~ 🎯
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    /// <summary>
    /// Gets the path to the plugin assembly this context manages~ 📦
    /// </summary>
    public string PluginPath => _pluginPath;

    /// <inheritdoc />
    /// <remarks>
    /// CopilotNote: We first check if the host already has the assembly loaded. If so,
    /// we return the host's version (shared types). Only if the host doesn't have it
    /// do we load it ourselves from the plugin's directory. This handles the type-identity
    /// problem cleanly without needing an explicit "shared assemblies" list~ 💖
    /// </remarks>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // 🔍 Step 1: Try to find the assembly in the Default (host) context.
        // If the host already has it, reuse that version for type-identity!
        try
        {
            var hostAssembly = Default.LoadFromAssemblyName(assemblyName);
            if (hostAssembly != null)
            {
                return null; // Returning null delegates to the Default context~ ✨
            }
        }
        catch (FileNotFoundException)
        {
            // Host doesn't have it — we need to load it ourselves below~ 🔧
        }

        // 🔍 Step 2: Try to resolve from the plugin's own dependency directory.
        var resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (resolvedPath != null)
        {
            return LoadFromAssemblyPath(resolvedPath);
        }

        // 🤷 Step 3: Give up — return null to trigger the default fallback chain.
        return null;
    }

    /// <inheritdoc />
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var resolvedPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return resolvedPath != null
            ? LoadUnmanagedDllFromPath(resolvedPath)
            : IntPtr.Zero;
    }
}

