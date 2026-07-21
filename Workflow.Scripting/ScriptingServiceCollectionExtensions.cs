// <copyright file="ScriptingServiceCollectionExtensions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Workflow.Scripting.Abstractions;
using Workflow.Scripting.Executors;

/// <summary>
/// 📜 Phase 3.1 — DI registration for the language-agnostic scripting core + the JavaScript executor~ ✨.
/// </summary>
public static class ScriptingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the scripting factory + the JavaScript (Jint) executor. Other languages layer on via
    /// <c>AddRoslynScripting()</c> (C#) and <c>AddLuaScripting()</c> (Lua)~ 📜.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddWorkflowScripting(this IServiceCollection services)
    {
        services.TryAddSingleton<IScriptExecutorFactory, ScriptExecutorFactory>();
        services.AddSingleton<IScriptExecutor, JavaScriptScriptExecutor>();
        return services;
    }
}
