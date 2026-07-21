// <copyright file="BuiltinModuleRegistration.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin;

using System.Collections.Generic;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.File;
using Workflow.Modules.Builtin.Flow;
using Workflow.Modules.Builtin.Http;
using Workflow.Modules.Builtin.Transform;

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
/// <para>
/// Phase 2.2.3a: Added parallel fan-out module — <c>builtin.parallel</c>~ 🌐
/// </para>
/// <para>
/// Phase 2.2.3b: Added fan-shaped pattern modules — <c>builtin.fanout</c>, <c>builtin.fanin</c>~ 🌟🪄
/// </para>
/// <para>
/// Phase 2.2.4: Added error handling modules — <c>builtin.trycatch</c>, <c>builtin.throw</c>~ 🛡️💥
/// </para>
/// <para>
/// Phase 2.3.0: Added HTTP request module — <c>builtin.http.request</c>~ 🌐
/// (requires <c>services.AddWorkflowModules()</c> at host startup for <c>IHttpClientFactory</c>)
/// </para>
/// <para>
/// Phase 2.3.6: Added webhook trigger module — <c>builtin.http.webhook</c>~ 🪝
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

        // Phase 2.2.3a — Parallel fan-out module~ 🌐
        new ParallelModule(),

        // Phase 2.2.3b — Fan-shaped pattern modules~ 🌟🪄
        new FanOutModule(),
        new FanInModule(),

        // Phase 2.2.4 — Error handling modules~ 🛡️💥
        new TryCatchModule(),
        new ThrowModule(),

        // Phase 2.3.0 — HTTP request module~ 🌐
        new HttpRequestModule(),

        // Phase 2.3.6 — Webhook trigger module~ 🪝
        new WebhookTriggerModule(),

        // Phase 2.5.a.1 — File read/write modules~ 📖✍️
        new FileReadModule(),
        new FileWriteModule(),

        // Phase 2.5.a.2 — Structured format modules (CSV/JSON/XML)~ 📊📄🏷️
        new CsvReadModule(),
        new CsvWriteModule(),
        new JsonReadModule(),
        new JsonWriteModule(),
        new XmlReadModule(),
        new XmlWriteModule(),

        // Phase 2.5.a.4 — Compression modules~ 🗜️📦
        new CompressModule(),
        new DecompressModule(),

        // Phase 2.6.a — Data transformation modules~ 🔄
        new DataMapModule(),
        new DataQueryModule(),
        new AggregateModule(),
        new DataJoinModule(),
        new JsonQueryModule(),
        new XmlQueryModule(),
        new JsonTransformModule(),
        new ValidateDataModule(),
        new StringTransformModule(),

        // Phase 3.1.4 — General-purpose script module~ 📜
        new Script.ScriptModule(),
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
