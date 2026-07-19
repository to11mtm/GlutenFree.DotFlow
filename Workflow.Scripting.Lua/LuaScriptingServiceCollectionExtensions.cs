// <copyright file="LuaScriptingServiceCollectionExtensions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Lua;

using Microsoft.Extensions.DependencyInjection;
using Workflow.Scripting.Abstractions;

/// <summary>
/// 🌙 Phase 3.1.3 — DI registration for the Lua (MoonSharp) executor~ ✨.
/// </summary>
public static class LuaScriptingServiceCollectionExtensions
{
    /// <summary>Registers the Lua script executor~ 🌙.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddLuaScripting(this IServiceCollection services)
    {
        services.AddSingleton<IScriptExecutor, LuaScriptExecutor>();
        return services;
    }
}
