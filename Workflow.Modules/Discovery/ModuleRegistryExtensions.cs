// <copyright file="ModuleRegistryExtensions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Discovery;

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🎀 Convenience extension methods for <see cref="IModuleRegistry"/> that integrate
/// with the module discovery system. Provides one-liner methods for scanning and
/// registering modules from assemblies~ ✨
/// </summary>
/// <remarks>
/// CopilotNote: These extensions make it super easy to wire up module discovery
/// during app startup! Just call <c>registry.DiscoverAndRegisterFrom(assembly)</c>
/// and you're done~ No need to manually create the discovery service. 💖
/// </remarks>
public static class ModuleRegistryExtensions
{
    /// <summary>
    /// Scans the given assembly for <see cref="IWorkflowModule"/> implementations
    /// and registers them into this registry. Uses the default <see cref="ModuleDiscovery"/>
    /// service internally~ 🔍
    /// </summary>
    /// <param name="registry">The module registry to populate.</param>
    /// <param name="assembly">The assembly to scan.</param>
    /// <param name="services">
    /// Optional service provider for DI-based constructor injection of discovered modules.
    /// </param>
    /// <param name="logger">
    /// Optional logger for discovery diagnostics. If null, logging is silenced.
    /// </param>
    /// <returns>The number of modules successfully registered. 🔢</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="registry"/> or <paramref name="assembly"/> is null.
    /// </exception>
    /// <example>
    /// <code>
    /// // 🌸 Register all modules from an assembly at startup~
    /// var registry = new InMemoryModuleRegistry();
    /// var count = registry.DiscoverAndRegisterFrom(typeof(MyModule).Assembly);
    /// Console.WriteLine($"Registered {count} modules!");
    /// </code>
    /// </example>
    public static int DiscoverAndRegisterFrom(
        this IModuleRegistry registry,
        Assembly assembly,
        IServiceProvider? services = null,
        ILogger<ModuleDiscovery>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(assembly);

        var discovery = new ModuleDiscovery(logger: logger);
        return discovery.DiscoverAndRegister(assembly, registry, services);
    }

    /// <summary>
    /// Scans the calling assembly for <see cref="IWorkflowModule"/> implementations
    /// and registers them into this registry. Super convenient for app startup! 🚀
    /// </summary>
    /// <param name="registry">The module registry to populate.</param>
    /// <param name="services">
    /// Optional service provider for DI-based constructor injection of discovered modules.
    /// </param>
    /// <param name="logger">
    /// Optional logger for discovery diagnostics.
    /// </param>
    /// <returns>The number of modules successfully registered. 🔢</returns>
    /// <example>
    /// <code>
    /// // 🌸 Register all modules from the current project~
    /// var registry = new InMemoryModuleRegistry();
    /// var count = registry.DiscoverAndRegisterFromCallingAssembly();
    /// </code>
    /// </example>
    /// <remarks>
    /// CopilotNote: Uses <see cref="Assembly.GetCallingAssembly"/> to determine
    /// which assembly to scan. Make sure this is called from the assembly that
    /// actually contains your modules~ ✨ The <c>[MethodImpl(NoInlining)]</c>
    /// attribute ensures the calling assembly is correctly resolved even with
    /// JIT inlining optimizations! 💖
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int DiscoverAndRegisterFromCallingAssembly(
        this IModuleRegistry registry,
        IServiceProvider? services = null,
        ILogger<ModuleDiscovery>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var callingAssembly = Assembly.GetCallingAssembly();
        return registry.DiscoverAndRegisterFrom(callingAssembly, services, logger);
    }
}

