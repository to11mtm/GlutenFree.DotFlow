// <copyright file="TransformModuleServiceCollectionExtensions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Transform;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// 🔄 DI registration helpers for the data-transformation built-in module family~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// Aggregated by <c>AddWorkflowModules()</c>. The transform modules themselves are stateless and
/// resolve the <c>IExpressionEvaluator</c> from <c>context.Services</c> at execution time, so this
/// extension currently registers no services — it exists as the family's future DI seam~ 🌸.
/// </para>
/// </remarks>
public static class TransformModuleServiceCollectionExtensions
{
    /// <summary>
    /// Registers services required by the data-transformation module family~ 🔄✨.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining~ 💖.</returns>
    public static IServiceCollection AddTransformModules(this IServiceCollection services)
    {
        // Expression evaluators are host-registered (Phase 2.2.5); the transform modules resolve
        // them per-call. No family-specific singletons needed yet~ 🌷
        return services;
    }
}
