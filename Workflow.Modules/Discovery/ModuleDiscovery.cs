// <copyright file="ModuleDiscovery.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Discovery;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Validation;

/// <summary>
/// 🔍 Default implementation of <see cref="IModuleDiscovery"/> that scans assemblies
/// for <see cref="IWorkflowModule"/> implementations via reflection. Handles DI-based
/// instantiation, validation, and graceful error handling like a pro~ ✨
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: This class is the heart of the module auto-discovery system!
/// It scans assemblies, finds candidate types, instantiates them, validates them,
/// and registers them — all while being super resilient to errors. One bad module
/// won't crash the whole app startup, it just gets skipped with a warning~ 💖
/// </para>
/// <para>
/// Discovery rules:
/// <list type="bullet">
/// <item>Only public, non-abstract, non-interface, concrete classes are considered</item>
/// <item>Must implement <see cref="IWorkflowModule"/></item>
/// <item>Must NOT be marked with <c>[WorkflowModule(Ignore = true)]</c></item>
/// <item>Must NOT be a generic type definition (open generics can't be instantiated!)</item>
/// </list>
/// </para>
/// </remarks>
public class ModuleDiscovery : IModuleDiscovery
{
    private readonly ModuleValidator _validator;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleDiscovery"/> class
    /// with optional validator and logger dependencies~ 🌸
    /// </summary>
    /// <param name="validator">
    /// Optional module validator. If null, a default instance is created.
    /// </param>
    /// <param name="logger">
    /// Optional logger for discovery diagnostics and warnings.
    /// </param>
    public ModuleDiscovery(ModuleValidator? validator = null, ILogger<ModuleDiscovery>? logger = null)
    {
        _validator = validator ?? new ModuleValidator();
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <inheritdoc />
    public IReadOnlyList<Type> DiscoverModuleTypes(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        _logger.LogDebug(
            "🔍 Scanning assembly '{AssemblyName}' for IWorkflowModule implementations~",
            assembly.GetName().Name);

        Type[] exportedTypes;

        try
        {
            // GetExportedTypes only returns public types — exactly what we want! ✨
            exportedTypes = assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Some types may fail to load (missing deps etc.) — use what we can! 🛡️
            _logger.LogWarning(
                ex,
                "⚠️ Some types in assembly '{AssemblyName}' could not be loaded. " +
                "Scanning available types only~",
                assembly.GetName().Name);

            exportedTypes = ex.Types
                .Where(t => t != null)
                .Cast<Type>()
                .ToArray();
        }

        var candidateTypes = exportedTypes
            .Where(IsModuleCandidate)
            .ToList();

        _logger.LogDebug(
            "✨ Found {Count} module candidate(s) in assembly '{AssemblyName}'~",
            candidateTypes.Count,
            assembly.GetName().Name);

        return candidateTypes.AsReadOnly();
    }

    /// <inheritdoc />
    public int DiscoverAndRegister(Assembly assembly, IModuleRegistry registry, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(registry);

        var types = DiscoverModuleTypes(assembly);

        if (types.Count == 0)
        {
            _logger.LogInformation(
                "📋 No workflow modules found in assembly '{AssemblyName}'~",
                assembly.GetName().Name);
            return 0;
        }

        _logger.LogInformation(
            "🚀 Discovered {Count} module type(s) in '{AssemblyName}', beginning registration~",
            types.Count,
            assembly.GetName().Name);

        var registeredCount = 0;

        foreach (var type in types)
        {
            try
            {
                var module = InstantiateModule(type, services);

                if (module == null)
                {
                    _logger.LogWarning(
                        "⚠️ Failed to instantiate module type '{TypeName}' — skipping~",
                        type.FullName);
                    continue;
                }

                // Validate the module before registration~ ✅
                var validationResult = _validator.Validate(module);
                if (!validationResult.IsValid)
                {
                    var errors = string.Join("; ", validationResult.Errors.Select(e => e.ToString()));
                    _logger.LogWarning(
                        "⚠️ Module '{ModuleId}' (type: {TypeName}) failed validation: {Errors} — skipping~",
                        module.ModuleId,
                        type.FullName,
                        errors);
                    continue;
                }

                // Check for duplicates — skip gracefully instead of crashing! 🛡️
                if (registry.HasModule(module.ModuleId))
                {
                    _logger.LogWarning(
                        "⚠️ Module '{ModuleId}' (type: {TypeName}) is already registered — skipping duplicate~",
                        module.ModuleId,
                        type.FullName);
                    continue;
                }

                registry.RegisterModule(module);
                registeredCount++;

                _logger.LogDebug(
                    "✅ Registered module '{ModuleId}' ({DisplayName}) from type '{TypeName}'~",
                    module.ModuleId,
                    module.DisplayName,
                    type.FullName);
            }
            catch (Exception ex)
            {
                // Catch-all: one bad module should NOT crash the whole discovery! 💖
                _logger.LogWarning(
                    ex,
                    "⚠️ Unexpected error processing module type '{TypeName}' — skipping~",
                    type.FullName);
            }
        }

        _logger.LogInformation(
            "🎉 Successfully registered {Registered}/{Total} module(s) from assembly '{AssemblyName}'~",
            registeredCount,
            types.Count,
            assembly.GetName().Name);

        return registeredCount;
    }

    /// <summary>
    /// Determines if a type is a valid <see cref="IWorkflowModule"/> candidate for discovery.
    /// Must be public, concrete, non-abstract, non-generic-definition, implement
    /// <see cref="IWorkflowModule"/>, and not marked with
    /// <c>[WorkflowModule(Ignore = true)]</c>~ 🎯
    /// </summary>
    /// <param name="type">The type to evaluate.</param>
    /// <returns>True if the type should be discovered.</returns>
    private bool IsModuleCandidate(Type type)
    {
        // Must be a public, concrete, non-abstract class~ 📦
        if (!type.IsClass || type.IsAbstract || !type.IsPublic)
        {
            return false;
        }

        // Can't instantiate open generic types! 🚫
        if (type.IsGenericTypeDefinition)
        {
            return false;
        }

        // Must implement IWorkflowModule~ 🔌
        if (!typeof(IWorkflowModule).IsAssignableFrom(type))
        {
            return false;
        }

        // Check for [WorkflowModule(Ignore = true)] — respect the flag! 🏷️
        var attribute = type.GetCustomAttribute<WorkflowModuleAttribute>();
        if (attribute is { Ignore: true })
        {
            _logger.LogDebug(
                "🚫 Skipping type '{TypeName}' — marked with [WorkflowModule(Ignore = true)]~",
                type.FullName);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Instantiates a module from its type, using DI when available or
    /// falling back to <see cref="Activator.CreateInstance(Type)"/>~ 🏭
    /// </summary>
    /// <param name="type">The module type to instantiate.</param>
    /// <param name="services">Optional service provider for DI. Can be null.</param>
    /// <returns>The instantiated module, or null if instantiation failed.</returns>
    private IWorkflowModule? InstantiateModule(Type type, IServiceProvider? services)
    {
        try
        {
            if (services != null)
            {
                // Use ActivatorUtilities for DI-aware instantiation~ 💉
                return (IWorkflowModule)ActivatorUtilities.CreateInstance(services, type);
            }

            // Fallback: parameterless constructor via Activator~ 🏗️
            var instance = Activator.CreateInstance(type);
            return instance as IWorkflowModule;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "⚠️ Could not instantiate module type '{TypeName}': {Message}~",
                type.FullName,
                ex.Message);
            return null;
        }
    }
}

