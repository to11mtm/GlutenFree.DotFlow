// <copyright file="FileSystemModuleServiceCollectionExtensions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.File;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Workflow.Modules.Builtin.File.Internal;

/// <summary>
/// 📁 DI registration helpers for the file-system built-in module family~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// Call <see cref="AddFileSystemModules"/> once in host startup — it's aggregated by
/// <c>AddWorkflowModules()</c> so most hosts get it for free~ 🌸.
/// </para>
/// <para>
/// CopilotNote: Phase 2.5.a.0. Unlike the database family, the local file family lives in
/// <c>Workflow.Modules</c> itself, so there's no circular-reference concern — it wires straight
/// into the top-level aggregate. Registers <see cref="IWorkflowPathValidator"/> (the sandbox gate)
/// and binds <see cref="FileSystemModuleOptions"/>~ 🛡️.
/// </para>
/// </remarks>
public static class FileSystemModuleServiceCollectionExtensions
{
    /// <summary>
    /// Registers the singleton services required by the file-system module family~ 📁✨.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining~ 💖.</returns>
    public static IServiceCollection AddFileSystemModules(this IServiceCollection services)
    {
        // Bind options so hosts can configure sandbox roots / blocked extensions~ ⚙️
        services.AddOptions<FileSystemModuleOptions>();

        // The path-security gate — DI-replaceable for hosts with fancier policy~ 🛡️
        services.TryAddSingleton<IWorkflowPathValidator, DefaultWorkflowPathValidator>();

        return services;
    }
}
