// <copyright file="AdvancedFlowPersistenceTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

using Akka.Actor;

namespace Workflow.Tests.Engine;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using LanguageExt;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Core.Abstractions;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Flow;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;
using Workflow.Persistence.Sqlite;
using Xunit;
/// <summary>
/// Phase 2.2.6 persistence integration tests for advanced flow control~
/// Uses WorkflowExecutor+TestProbe pattern (same as TryCatchModuleTests)~ uwu
/// </summary>
public sealed class AdvancedFlowPersistenceTests : TestKit, IAsyncLifetime
{
    private SqlitePersistenceProvider _provider = null!;
    private SqliteConnection _heldConnection = null!;
    public async Task InitializeAsync()
    {
        var dbName = $"adv_flow_{Guid.NewGuid():N}";
        var cs = $"Filename=file:memdb-{dbName}.db;Mode=Memory;Cache=Shared";
        _heldConnection = new SqliteConnection(cs);
        await _heldConnection.OpenAsync();
        _provider = new SqlitePersistenceProvider(cs);
        await _provider.InitializeAsync();
    }
    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _heldConnection.DisposeAsync();
    }
    [Fact]
    public async Task AdvancedFlow_Condition_TrueBranch_PersistsOnlyTruePathNodes()
    {
        var eid = Guid.NewGuid();
        var sp = BuildSp(BuildReg(new ConditionalModule(), new QuickLogModule("log.true"), new QuickLogModule("log.false")));
        var def = BuildCondWf(true);
        var probe = CreateTestProbe("p-cond-t");
        var exec = probe.ChildActorOf(WorkflowExecutor.Props(eid, def, new Dictionary<string, object?>(), sp), "e-cond-t");
        exec.Tell(new StartExecution(eid));
        probe.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(10))
             .Should().BeOfType<WorkflowCompleted>();
        await Task.Delay(200).ConfigureAwait(false);
        var r = await _provider.ExecutionHistory.GetNodeExecutionsAsync(eid);
        r.Should().Contain(x => x.NodeId == "cond" && x.State == NodeExecutionState.Completed);
        r.Should().Contain(x => x.NodeId == "true_node" && x.State == NodeExecutionState.Completed);
        r.Should().NotContain(x => x.NodeId == "false_node", because: "skipped, not persisted");
    }
    [Fact]
    public async Task AdvancedFlow_Condition_FalseBranch_PersistsOnlyFalsePathNodes()
    {
        var eid = Guid.NewGuid();
        var sp = BuildSp(BuildReg(new ConditionalModule(), new QuickLogModule("log.true"), new QuickLogModule("log.false")));
        var def = BuildCondWf(false);
        var probe = CreateTestProbe("p-cond-f");
        var exec = probe.ChildActorOf(WorkflowExecutor.Props(eid, def, new Dictionary<string, object?>(), sp), "e-cond-f");
        exec.Tell(new StartExecution(eid));
        probe.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(10))
             .Should().BeOfType<WorkflowCompleted>();
        await Task.Delay(200).ConfigureAwait(false);
        var r = await _provider.ExecutionHistory.GetNodeExecutionsAsync(eid);
        r.Should().Contain(x => x.NodeId == "false_node" && x.State == NodeExecutionState.Completed);
        r.Should().NotContain(x => x.NodeId == "true_node", because: "skipped, not persisted");
    }
    [Fact]
    public async Task AdvancedFlow_TryCatch_SuccessPath_AllNodesPersistedCorrectly()
    {
        var eid = Guid.NewGuid();
        var sp = BuildSp(BuildReg(new TryCatchModule(),
            new QuickLogModule("log.try"), new QuickLogModule("log.finally"), new QuickLogModule("log.post")));
        var def = BuildTcWf(false, false, true);
        var probe = CreateTestProbe("p-tc-ok");
        var exec = probe.ChildActorOf(WorkflowExecutor.Props(eid, def, new Dictionary<string, object?>(), sp), "e-tc-ok");
        exec.Tell(new StartExecution(eid));
        probe.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(10))
             .Should().BeOfType<WorkflowCompleted>(because: "try succeeds");
        await Task.Delay(300).ConfigureAwait(false);
        var r = await _provider.ExecutionHistory.GetNodeExecutionsAsync(eid);
        r.Should().Contain(x => x.NodeId == "tc" && x.State == NodeExecutionState.Completed);
        r.Should().Contain(x => x.NodeId == "try_node" && x.State == NodeExecutionState.Completed);
        r.Should().Contain(x => x.NodeId == "finally_node" && x.State == NodeExecutionState.Completed);
        r.Should().Contain(x => x.NodeId == "post_node" && x.State == NodeExecutionState.Completed);
    }
    [Fact]
    public async Task AdvancedFlow_TryCatch_ErrorPath_CatchAndFinallyPersistedAndWorkflowCompletes()
    {
        var eid = Guid.NewGuid();
        var sp = BuildSp(BuildReg(new TryCatchModule(), new ThrowModule(),
            new QuickLogModule("log.catch"), new QuickLogModule("log.finally"), new QuickLogModule("log.post")));
        var def = BuildTcWf(true, true, true);
        var probe = CreateTestProbe("p-tc-err");
        var exec = probe.ChildActorOf(WorkflowExecutor.Props(eid, def, new Dictionary<string, object?>(), sp), "e-tc-err");
        exec.Tell(new StartExecution(eid));
        probe.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(10))
             .Should().BeOfType<WorkflowCompleted>(because: "error caught");
        await Task.Delay(300).ConfigureAwait(false);
        var r = await _provider.ExecutionHistory.GetNodeExecutionsAsync(eid);
        r.Should().Contain(x => x.NodeId == "tc" && x.State == NodeExecutionState.Completed);
        r.Should().Contain(x => x.NodeId == "throw_node");
        r.Should().Contain(x => x.NodeId == "catch_node" && x.State == NodeExecutionState.Completed);
        r.Should().Contain(x => x.NodeId == "finally_node" && x.State == NodeExecutionState.Completed);
        r.Should().Contain(x => x.NodeId == "post_node" && x.State == NodeExecutionState.Completed);
    }
    [Fact]
    public async Task AdvancedFlow_TryCatch_Rethrow_WorkflowFails_FinallyStillPersisted()
    {
        var eid = Guid.NewGuid();
        var sp = BuildSp(BuildReg(new TryCatchModule(), new ThrowModule(), new QuickLogModule("log.finally")));
        var def = BuildTcRethrowWf();
        var probe = CreateTestProbe("p-tc-rethrow");
        var exec = probe.ChildActorOf(WorkflowExecutor.Props(eid, def, new Dictionary<string, object?>(), sp), "e-tc-rethrow");
        exec.Tell(new StartExecution(eid));
        probe.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(10))
             .Should().BeOfType<WorkflowFailed>(because: "rethrow=true escalates");
        await Task.Delay(300).ConfigureAwait(false);
        var r = await _provider.ExecutionHistory.GetNodeExecutionsAsync(eid);
        r.Should().Contain(x => x.NodeId == "finally_node", because: "finally ran before rethrow");
    }
    [Fact]
    public async Task AdvancedFlow_Combined_LowScore_CaughtByTryCatch_WorkflowCompletes()
    {
        var eid = Guid.NewGuid();
        var sp = BuildSp(BuildReg(
            new ConditionalModule(), new TryCatchModule(), new ThrowModule(),
            new QuickLogModule("log.approved"), new QuickLogModule("log.denied"),
            new QuickLogModule("log.finally"), new QuickLogModule("log.end")));
        var def = BuildCombinedWf(400);
        var probe = CreateTestProbe("p-combined");
        var exec = probe.ChildActorOf(WorkflowExecutor.Props(eid, def, new Dictionary<string, object?>(), sp), "e-combined");
        exec.Tell(new StartExecution(eid));
        probe.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(15))
             .Should().BeOfType<WorkflowCompleted>(because: "trycatch catches the throw");
        await Task.Delay(300).ConfigureAwait(false);
        var r = await _provider.ExecutionHistory.GetNodeExecutionsAsync(eid);
        r.Should().NotBeEmpty();
        r.Should().Contain(x => x.NodeId == "tc" && x.State == NodeExecutionState.Completed);
        r.Should().Contain(x => x.NodeId == "finally_node" && x.State == NodeExecutionState.Completed);
        r.Should().Contain(x => x.NodeId == "end_node" && x.State == NodeExecutionState.Completed);
    }
    // helpers
    private static InMemoryModuleRegistry BuildReg(params IWorkflowModule[] mods)
    {
        var r = new InMemoryModuleRegistry(skipValidation: true);
        foreach (var m in mods) r.RegisterModule(m);
        return r;
    }
    private IServiceProvider BuildSp(IModuleRegistry reg)
    {
        var s = new ServiceCollection();
        s.AddSingleton<WorkflowValidator>();
        s.AddSingleton<IPersistenceProvider>(_provider);
        s.AddSingleton<IWorkflowRepository>(_provider.Workflows);
        s.AddSingleton<IExecutionHistoryRepository>(_provider.ExecutionHistory);
        s.AddSingleton<IVariableStore>(_provider.Variables);
        s.AddSingleton<IModuleRegistry>(reg);
        return s.BuildServiceProvider();
    }
    private static WorkflowDefinition BuildCondWf(bool cond)
    {
        var p = new Dictionary<string, JsonElement>
            { ["condition"] = JsonSerializer.SerializeToElement(cond ? "true" : "false") }.ToHashMap();
        return new WorkflowDefinition(Guid.NewGuid(), "cond-test", null, new Version(1,0),
            Arr.create(new NodeDefinition("cond","builtin.condition","Cond",p),
                       new NodeDefinition("true_node","log.true","T",HashMap<string,JsonElement>.Empty),
                       new NodeDefinition("false_node","log.false","F",HashMap<string,JsonElement>.Empty)),
            Arr.create(new ConnectionDefinition("cond","true","true_node","input"),
                       new ConnectionDefinition("cond","false","false_node","input")),
            HashMap<string,VariableDefinition>.Empty);
    }
    private static WorkflowDefinition BuildTcWf(bool throwInTry, bool includeCatch, bool includeFinally)
    {
        var throwP = new Dictionary<string,JsonElement>
            { ["errorType"] = JsonSerializer.SerializeToElement("TestError"),
              ["message"]   = JsonSerializer.SerializeToElement("test") }.ToHashMap();
        var nodes = new List<NodeDefinition>
        {
            new("tc","builtin.trycatch","TC",HashMap<string,JsonElement>.Empty),
            throwInTry
                ? new NodeDefinition("throw_node","builtin.throw","Thr",throwP)
                : new NodeDefinition("try_node","log.try","Try",HashMap<string,JsonElement>.Empty),
        };
        var conns = new List<ConnectionDefinition>
            { new("tc","try", throwInTry ? "throw_node" : "try_node","input") };
        if (includeCatch) { nodes.Add(new("catch_node","log.catch","Cat",HashMap<string,JsonElement>.Empty)); conns.Add(new("tc","catch","catch_node","input")); }
        if (includeFinally) { nodes.Add(new("finally_node","log.finally","Fin",HashMap<string,JsonElement>.Empty)); conns.Add(new("tc","finally","finally_node","input")); }
        nodes.Add(new("post_node","log.post","Post",HashMap<string,JsonElement>.Empty));
        conns.Add(new("tc","done","post_node","input"));
        return new WorkflowDefinition(Guid.NewGuid(),"tc-test",null,new Version(1,0),
            Arr.create(nodes.ToArray()),Arr.create(conns.ToArray()),HashMap<string,VariableDefinition>.Empty);
    }
    private static WorkflowDefinition BuildTcRethrowWf()
    {
        var tcp = new Dictionary<string,JsonElement> { ["rethrow"]=JsonSerializer.SerializeToElement(true) }.ToHashMap();
        var thr = new Dictionary<string,JsonElement> { ["errorType"]=JsonSerializer.SerializeToElement("Err"), ["message"]=JsonSerializer.SerializeToElement("boom") }.ToHashMap();
        return new WorkflowDefinition(Guid.NewGuid(),"tc-rethrow",null,new Version(1,0),
            Arr.create(new NodeDefinition("tc","builtin.trycatch","TC",tcp),
                       new NodeDefinition("throw_node","builtin.throw","Thr",thr),
                       new NodeDefinition("finally_node","log.finally","Fin",HashMap<string,JsonElement>.Empty)),
            Arr.create(new ConnectionDefinition("tc","try","throw_node","input"),
                       new ConnectionDefinition("tc","finally","finally_node","input")),
            HashMap<string,VariableDefinition>.Empty);
    }
    private static WorkflowDefinition BuildCombinedWf(int score)
    {
        var cv = score >= 600;
        var cp = new Dictionary<string,JsonElement> { ["condition"]=JsonSerializer.SerializeToElement(cv?"true":"false") }.ToHashMap();
        var tp = new Dictionary<string,JsonElement> { ["errorType"]=JsonSerializer.SerializeToElement("Low"), ["message"]=JsonSerializer.SerializeToElement($"score {score}") }.ToHashMap();
        return new WorkflowDefinition(Guid.NewGuid(),"combined",null,new Version(1,0),
            Arr.create(
                new NodeDefinition("tc","builtin.trycatch","TC",HashMap<string,JsonElement>.Empty),
                new NodeDefinition("cond","builtin.condition","Cond",cp),
                new NodeDefinition("approved_node","log.approved","App",HashMap<string,JsonElement>.Empty),
                new NodeDefinition("throw_node","builtin.throw","Thr",tp),
                new NodeDefinition("catch_node","log.denied","Den",HashMap<string,JsonElement>.Empty),
                new NodeDefinition("finally_node","log.finally","Fin",HashMap<string,JsonElement>.Empty),
                new NodeDefinition("end_node","log.end","End",HashMap<string,JsonElement>.Empty)),
            Arr.create(
                new ConnectionDefinition("tc","try","cond","input"),
                new ConnectionDefinition("cond","true","approved_node","input"),
                new ConnectionDefinition("cond","false","throw_node","input"),
                new ConnectionDefinition("tc","catch","catch_node","input"),
                new ConnectionDefinition("tc","finally","finally_node","input"),
                new ConnectionDefinition("tc","done","end_node","input")),
            HashMap<string,VariableDefinition>.Empty);
    }
    private sealed class QuickLogModule : IWorkflowModule
    {
        public QuickLogModule(string id) => ModuleId = id;
        public string ModuleId { get; }
        public string DisplayName => "QL";
        public string Category => "Test";
        public string Description => "ok";
        public string Icon => "📝";
        public Version Version => new(1,0,0);
        public ModuleSchema Schema => new(
            Inputs: Arr.create(PortDefinition.Create<object>("input",false)),
            Outputs: Arr.create(PortDefinition.Create<object>("output",false)),
            Properties: Arr<ModulePropertyDefinition>.Empty);
        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
            => Task.FromResult(ModuleResult.Ok(new Dictionary<string,object?>{["done"]=true}));
    }
}
