// <copyright file="BuiltinModuleRegistration.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin;

using System.Collections.Generic;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Flow;

/// <summary>
/// 📦 Convenience class for registering all built-in modules at once~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Use <see cref="RegisterAll"/> to bulk-register into an
/// <see cref="IModuleRegistry"/>, or <see cref="GetAll"/> to get a flat list
/// without a registry dependency~ 🌸.
/// </para>
/// <para>
/// Phase 2.2.1: Added flow control modules — <c>builtin.condition</c> and <c>builtin.switch</c>~ 🔀🔢
/// </para>
/// <para>
/// Phase 2.2.2: Added loop control modules — <c>builtin.loop.foreach</c>,
/// <c>builtin.loop.while</c>, <c>builtin.break</c>, <c>builtin.continue</c>~ 🔁🌀⏹️⏭️
/// </para>
/// </remarks>
public static class BuiltinModules
{
    /// <summary>
    /// Returns all built-in module instances~ 📦✨.
    /// </summary>
    public static IReadOnlyList<IWorkflowModule> GetAll() => new IWorkflowModule[]
    {
        // Core utility modules~ 🛠️
        new PassThroughModule(),
        new LogModule(),
        new DelayModule(),
        new SetVariableModule(),
        new GetVariableModule(),

        // Phase 2.2.1 — Conditional branching modules~ 🔀
        new ConditionalModule(),
        new SwitchModule(),

        // Phase 2.2.2 — Loop control modules~ 🔁
        new ForEachModule(),
        new WhileModule(),
        new BreakModule(),
        new ContinueModule(),
    };

    /// <summary>
    /// Registers all built-in modules into the given registry~ 📦💖.
    /// </summary>
    /// <param name="registry">The module registry to populate.</param>
    public static void RegisterAll(IModuleRegistry registry)
    {
        foreach (var module in GetAll())
        {
            registry.RegisterModule(module);
        }
    }
}
