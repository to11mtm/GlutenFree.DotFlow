// <copyright file="BuiltinModuleIntegrationTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin;
using Workflow.Modules.Builtin.Flow;
using Workflow.Modules.Builtin.Http;
using Workflow.Modules.Discovery;
using Xunit;

/// <summary>
/// 🎯 Phase 1.5.5 — Integration tests for built-in modules (unit-level, no Akka)~ ✨💖
/// Validates module chaining, registration, and discovery across all builtin modules.
/// </summary>
public sealed class BuiltinModuleIntegrationTests
{
    #region Helpers 🛠️

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static ModuleExecutionContext BuildContext(
        Dictionary<string, object?>? properties = null,
        Dictionary<string, object?>? inputs = null,
        Dictionary<string, object?>? variables = null)
    {
        return new ModuleExecutionContext
        {
            Inputs = inputs ?? new Dictionary<string, object?>(),
            Properties = properties ?? new Dictionary<string, object?>(),
            Variables = variables ?? new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new EmptyServiceProvider(),
            ExecutionId = Guid.NewGuid(),
            NodeId = "integration-node",
        };
    }

    #endregion

    #region Registration & Discovery Tests 📦

    /// <summary>
    /// All 18 builtin modules should register successfully via <see cref="BuiltinModules.RegisterAll"/>~ 📦
    /// Phase 2.2.1 added builtin.condition and builtin.switch~ 🔀🔢
    /// Phase 2.2.2 added builtin.loop.foreach, builtin.loop.while, builtin.break, builtin.continue~ 🔁🌀
    /// Phase 2.2.3a added builtin.parallel~ 🌐
    /// Phase 2.2.3b added builtin.fanout and builtin.fanin~ 🔀🔁
    /// Phase 2.2.4 added builtin.trycatch and builtin.throw~ 🛡️💥
    /// Phase 2.3.0 added builtin.http.request~ 🌐
    /// Phase 2.3.6 added builtin.http.webhook~ 🪝
    /// </summary>
    [Fact]
    public void RegisterAll_ShouldRegisterAllBuiltinModules()
    {
        var registry = new InMemoryModuleRegistry();

        BuiltinModules.RegisterAll(registry);

        registry.HasModule("builtin.passthrough").Should().BeTrue();
        registry.HasModule("builtin.log").Should().BeTrue();
        registry.HasModule("builtin.delay").Should().BeTrue();
        registry.HasModule("builtin.setvariable").Should().BeTrue();
        registry.HasModule("builtin.getvariable").Should().BeTrue();
        registry.HasModule("builtin.condition").Should().BeTrue("Phase 2.2.1 condition module must register~ 🔀");
        registry.HasModule("builtin.switch").Should().BeTrue("Phase 2.2.1 switch module must register~ 🔢");
        registry.HasModule("builtin.loop.foreach").Should().BeTrue("Phase 2.2.2 foreach module must register~ 🔁");
        registry.HasModule("builtin.loop.while").Should().BeTrue("Phase 2.2.2 while module must register~ 🌀");
        registry.HasModule("builtin.break").Should().BeTrue("Phase 2.2.2 break module must register~ ⏹️");
        registry.HasModule("builtin.continue").Should().BeTrue("Phase 2.2.2 continue module must register~ ⏭️");
        registry.HasModule("builtin.parallel").Should().BeTrue("Phase 2.2.3a parallel module must register~ 🌐");
        registry.HasModule("builtin.fanout").Should().BeTrue("Phase 2.2.3b fanout module must register~ 🔀");
        registry.HasModule("builtin.fanin").Should().BeTrue("Phase 2.2.3b fanin module must register~ 🔁");
        registry.HasModule("builtin.trycatch").Should().BeTrue("Phase 2.2.4 trycatch module must register~ 🛡️");
        registry.HasModule("builtin.throw").Should().BeTrue("Phase 2.2.4 throw module must register~ 💥");
        registry.HasModule("builtin.http.request").Should().BeTrue("Phase 2.3.0 http.request must register~ 🌐");
        registry.HasModule("builtin.http.webhook").Should().BeTrue("Phase 2.3.6 http.webhook must register~ 🪝");
        registry.HasModule("builtin.file.read").Should().BeTrue("Phase 2.5.a.1 file.read must register~ 📖");
        registry.HasModule("builtin.file.write").Should().BeTrue("Phase 2.5.a.1 file.write must register~ ✍️");
        registry.HasModule("builtin.file.csv.read").Should().BeTrue("Phase 2.5.a.2 csv.read must register~ 📊");
        registry.HasModule("builtin.file.json.read").Should().BeTrue("Phase 2.5.a.2 json.read must register~ 📄");
        registry.HasModule("builtin.file.xml.read").Should().BeTrue("Phase 2.5.a.2 xml.read must register~ 🏷️");
        registry.HasModule("builtin.file.compress").Should().BeTrue("Phase 2.5.a.4 compress must register~ 🗜️");
        registry.HasModule("builtin.file.decompress").Should().BeTrue("Phase 2.5.a.4 decompress must register~ 📦");
        registry.HasModule("builtin.transform.map").Should().BeTrue("Phase 2.6.a.1 transform.map must register~ 🔄");
        registry.HasModule("builtin.transform.query").Should().BeTrue("Phase 2.6.a.2 transform.query must register~ 🔍");
        registry.HasModule("builtin.transform.aggregate").Should().BeTrue("Phase 2.6.a.2 transform.aggregate must register~ 📊");
        registry.HasModule("builtin.transform.join").Should().BeTrue("Phase 2.6.a.2 transform.join must register~ 🔗");
        registry.HasModule("builtin.transform.jsonquery").Should().BeTrue("Phase 2.6.a.3 transform.jsonquery must register~ 🎯");
        registry.HasModule("builtin.transform.xmlquery").Should().BeTrue("Phase 2.6.a.3 transform.xmlquery must register~ 🏷️");
        registry.HasModule("builtin.transform.json").Should().BeTrue("Phase 2.6.a.3 transform.json must register~ 📝");
        registry.HasModule("builtin.transform.validate").Should().BeTrue("Phase 2.6.a.4 transform.validate must register~ ✅");
        registry.HasModule("builtin.transform.string").Should().BeTrue("Phase 2.6.a.5 transform.string must register~ 📝");
        registry.GetAllModules().Should().HaveCount(37, because: "37 builtin modules after Phase 2.6.a~ 💖");
    }

    /// <summary>
    /// <see cref="BuiltinModules.GetAll"/> should return all 18 builtin modules~ 📦
    /// Phase 2.2.1 added builtin.condition and builtin.switch~ 🔀🔢
    /// Phase 2.2.2 added loop modules~ 🔁
    /// Phase 2.2.3a added builtin.parallel~ 🌐
    /// Phase 2.2.3b added builtin.fanout and builtin.fanin~ 🔀🔁
    /// Phase 2.2.4 added builtin.trycatch and builtin.throw~ 🛡️💥
    /// Phase 2.3.0 added builtin.http.request~ 🌐
    /// Phase 2.3.6 added builtin.http.webhook~ 🪝
    /// </summary>
    [Fact]
    public void GetAll_ShouldReturnFiveModules()
    {
        var modules = BuiltinModules.GetAll();
        modules.Should().HaveCount(37, because: "37 builtin modules after Phase 2.6.a~ 💖");
        modules.Select(m => m.ModuleId).Should().BeEquivalentTo(
            "builtin.passthrough", "builtin.log", "builtin.delay",
            "builtin.setvariable", "builtin.getvariable",
            "builtin.condition", "builtin.switch",
            "builtin.loop.foreach", "builtin.loop.while",
            "builtin.break", "builtin.continue",
            "builtin.parallel",
            "builtin.fanout", "builtin.fanin",
            "builtin.trycatch", "builtin.throw",
            "builtin.http.request", "builtin.http.webhook",
            "builtin.file.read", "builtin.file.write",
            "builtin.file.csv.read", "builtin.file.csv.write",
            "builtin.file.json.read", "builtin.file.json.write",
            "builtin.file.xml.read", "builtin.file.xml.write",
            "builtin.file.compress", "builtin.file.decompress",
            "builtin.transform.map", "builtin.transform.query",
            "builtin.transform.aggregate", "builtin.transform.join",
            "builtin.transform.jsonquery", "builtin.transform.xmlquery",
            "builtin.transform.json", "builtin.transform.validate",
            "builtin.transform.string");
    }

    /// <summary>
    /// <see cref="ModuleDiscovery"/> should auto-discover all 18 builtin modules from the assembly~ 🔍
    /// Phase 2.2.1 added condition and switch modules~ 🔀🔢
    /// Phase 2.2.2 added loop modules~ 🔁
    /// Phase 2.2.3a added builtin.parallel~ 🌐
    /// Phase 2.2.3b added fanout and fanin modules~ 🔀🔁
    /// Phase 2.2.4 added trycatch and throw modules~ 🛡️💥
    /// Phase 2.3.0 added builtin.http.request~ 🌐
    /// Phase 2.3.6 added builtin.http.webhook~ 🪝
    /// </summary>
    [Fact]
    public void ModuleDiscovery_ShouldFindAllBuiltinModules()
    {
        var discovery = new ModuleDiscovery();
        var types = discovery.DiscoverModuleTypes(typeof(LogModule).Assembly);

        types.Should().Contain(typeof(PassThroughModule));
        types.Should().Contain(typeof(LogModule));
        types.Should().Contain(typeof(DelayModule));
        types.Should().Contain(typeof(SetVariableModule));
        types.Should().Contain(typeof(GetVariableModule));
        types.Should().Contain(typeof(ConditionalModule), "Phase 2.2.1 condition module must be discovered~ 🔀");
        types.Should().Contain(typeof(SwitchModule), "Phase 2.2.1 switch module must be discovered~ 🔢");
        types.Should().Contain(typeof(ForEachModule), "Phase 2.2.2 foreach module must be discovered~ 🔁");
        types.Should().Contain(typeof(WhileModule), "Phase 2.2.2 while module must be discovered~ 🌀");
        types.Should().Contain(typeof(BreakModule), "Phase 2.2.2 break module must be discovered~ ⏹️");
        types.Should().Contain(typeof(ContinueModule), "Phase 2.2.2 continue module must be discovered~ ⏭️");
        types.Should().Contain(typeof(ParallelModule), "Phase 2.2.3a parallel module must be discovered~ 🌐");
        types.Should().Contain(typeof(FanOutModule), "Phase 2.2.3b fanout module must be discovered~ 🔀");
        types.Should().Contain(typeof(FanInModule), "Phase 2.2.3b fanin module must be discovered~ 🔁");
        types.Should().Contain(typeof(TryCatchModule), "Phase 2.2.4 trycatch module must be discovered~ 🛡️");
        types.Should().Contain(typeof(ThrowModule), "Phase 2.2.4 throw module must be discovered~ 💥");
        types.Should().Contain(typeof(HttpRequestModule), "Phase 2.3.0 http.request must be discovered~ 🌐");
        types.Should().Contain(typeof(WebhookTriggerModule), "Phase 2.3.6 http.webhook must be discovered~ 🪝");
    }

    #endregion

    #region Module Chaining Tests 🔗

    /// <summary>
    /// SetVariable → GetVariable chain: variable set by one module should be readable by the next~ 💾🔍
    /// </summary>
    [Fact]
    public async Task SetVariable_Then_GetVariable_ShouldPersistValue()
    {
        var setModule = new SetVariableModule();
        var getModule = new GetVariableModule();
        var variables = new Dictionary<string, object?>();

        // Step 1: SetVariable("greeting", "UwU")
        var setCtx = BuildContext(
            properties: new Dictionary<string, object?> { ["name"] = "greeting", ["value"] = "UwU" },
            variables: variables);
        var setResult = await setModule.ExecuteAsync(setCtx);
        setResult.Success.Should().BeTrue();

        // Simulate WorkflowExecutor applying VariableUpdates
        foreach (var (k, v) in setResult.VariableUpdates!)
        {
            variables[k] = v;
        }

        // Step 2: GetVariable("greeting")
        var getCtx = BuildContext(
            properties: new Dictionary<string, object?> { ["name"] = "greeting" },
            variables: variables);
        var getResult = await getModule.ExecuteAsync(getCtx);

        getResult.Success.Should().BeTrue();
        getResult.Outputs["value"].Should().Be("UwU");
        getResult.Outputs["exists"].Should().Be(true);
    }

    /// <summary>
    /// Log → SetVariable chain: log timestamp captured as variable~ 📝💾
    /// </summary>
    [Fact]
    public async Task Log_Then_SetVariable_ShouldCaptureTimestamp()
    {
        var logModule = new LogModule();
        var setModule = new SetVariableModule();

        // Step 1: Log a message
        var logCtx = BuildContext(
            properties: new Dictionary<string, object?> { ["message"] = "hello~ 💖" });
        var logResult = await logModule.ExecuteAsync(logCtx);
        logResult.Success.Should().BeTrue();
        var timestamp = logResult.Outputs["timestamp"];

        // Step 2: SetVariable with timestamp from log output via input port
        var setCtx = BuildContext(
            properties: new Dictionary<string, object?> { ["name"] = "lastLogTime" },
            inputs: new Dictionary<string, object?> { ["value"] = timestamp });
        var setResult = await setModule.ExecuteAsync(setCtx);

        setResult.Success.Should().BeTrue();
        setResult.VariableUpdates!["lastLogTime"].Should().Be(timestamp);
    }

    /// <summary>
    /// GetVariable → Log chain: variable value used in log message~ 🔍📝
    /// </summary>
    [Fact]
    public async Task GetVariable_Then_Log_ShouldOutputVariableValue()
    {
        var getModule = new GetVariableModule();
        var logModule = new LogModule();

        // Step 1: GetVariable with existing value
        var getCtx = BuildContext(
            properties: new Dictionary<string, object?> { ["name"] = "userName" },
            variables: new Dictionary<string, object?> { ["userName"] = "Ami-chan" });
        var getResult = await getModule.ExecuteAsync(getCtx);
        getResult.Success.Should().BeTrue();
        getResult.Outputs["value"].Should().Be("Ami-chan");

        // Step 2: Log using the retrieved value
        var logCtx = BuildContext(
            properties: new Dictionary<string, object?> { ["message"] = $"Hello {getResult.Outputs["value"]}~ 💖" });
        var logResult = await logModule.ExecuteAsync(logCtx);

        logResult.Success.Should().BeTrue();
        ((string)logResult.Outputs["message"]!).Should().Contain("Ami-chan");
    }

    #endregion
}

