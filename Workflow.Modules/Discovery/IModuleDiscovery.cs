// <copyright file="IModuleDiscovery.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Discovery;

using System;
using System.Collections.Generic;
using System.Reflection;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🔍 Service for automatically discovering <see cref="IWorkflowModule"/> implementations
/// within assemblies via reflection scanning. Supports DI-based instantiation,
/// validation, and graceful error handling~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: This is the main entry point for assembly scanning! Use
/// <see cref="DiscoverModuleTypes"/> to just find the types, or use
/// <see cref="DiscoverAndRegister"/> to find, instantiate, validate, and register
/// in one go~ Super convenient! 💖.
/// </para>
/// </remarks>
public interface IModuleDiscovery
{
    /// <summary>
    /// Scans an assembly and returns all types that are valid <see cref="IWorkflowModule"/>
    /// candidates (public, non-abstract, concrete classes). Respects the
    /// <see cref="WorkflowModuleAttribute.Ignore"/> flag to exclude marked types. 🔎.
    /// </summary>
    /// <param name="assembly">The assembly to scan for module types.</param>
    /// <returns>
    /// A read-only list of discovered types implementing <see cref="IWorkflowModule"/>.
    /// May be empty if no candidates are found~ 📋.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="assembly"/> is null.
    /// </exception>
    public IReadOnlyList<Type> DiscoverModuleTypes(Assembly assembly);

    /// <summary>
    /// Discovers, instantiates, validates, and registers all <see cref="IWorkflowModule"/>
    /// implementations found in the given assembly. Returns the count of successfully
    /// registered modules. Invalid or duplicate modules are skipped with warnings~ 🚀.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <param name="registry">The module registry to register discovered modules into.</param>
    /// <param name="services">
    /// Optional service provider for DI-based constructor injection.
    /// When null, falls back to <see cref="Activator.CreateInstance(Type)"/>~ 💉.
    /// </param>
    /// <returns>The number of modules successfully registered. 🔢.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="assembly"/> or <paramref name="registry"/> is null.
    /// </exception>
    /// <remarks>
    /// CopilotNote: This method is designed to be resilient! It will:
    /// <list type="bullet">
    /// <item>Skip types marked with <c>[WorkflowModule(Ignore = true)]</c></item>
    /// <item>Skip modules that fail <c>ModuleValidator</c> validation</item>
    /// <item>Skip modules with duplicate IDs (already registered)</item>
    /// <item>Log warnings for all skipped modules instead of crashing</item>
    /// </list>
    /// This makes it safe to call during app startup without risking a crash
    /// from one bad module taking down the whole system~ 💖.
    /// </remarks>
    public int DiscoverAndRegister(Assembly assembly, IModuleRegistry registry, IServiceProvider? services = null);
}
