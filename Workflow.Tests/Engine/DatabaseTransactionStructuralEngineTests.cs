// <copyright file="DatabaseTransactionStructuralEngineTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using LanguageExt;
using LinqToDB.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Database;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Builtin;
using Workflow.Modules.Database.Configuration;
using Workflow.Modules.Database.Connections;
using Workflow.Modules.Database.Providers;
using Xunit;

/// <summary>
/// 💼 Structural transaction engine tests using temp-file SQLite databases~ ✨💖.
/// </summary>
public sealed class DatabaseTransactionStructuralEngineTests : TestKit
{
    [Fact]
    public void TransactionBody_AllNodesSucceed_CommitsAndRoutesCommitted()
    {
        var dbPath = NewDbPath();
        try
        {
            var services = BuildServices(("TestDb", dbPath));
            Seed(services, "TestDb");

            var definition = Workflow(
                Node("insert", "builtin.database.execute", Props(
                    ("connectionId", "TestDb"),
                    ("command", "INSERT INTO users (id, name) VALUES (10, 'committed')"))),
                Connections(
                    Conn("txn", "transactionBody", "insert")));

            var completed = RunTransaction(definition, services, "TestDb");

            CountUsers(services, "TestDb").Should().Be(1, "the body insert should commit~ ✅");
            completed.Outputs["success"].Should().Be(true);
            completed.Outputs["committed"].Should().Be(true);
            completed.Outputs["rolledBack"].Should().Be(false);
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public void WorkflowExecutor_TransactionRequest_RoutesCommittedPort()
    {
        var dbPath = NewDbPath();
        try
        {
            var services = BuildServices(("TestDb", dbPath));
            Seed(services, "TestDb");

            var definition = Workflow(
                Node("txn", "builtin.database.transaction", Props(("connectionId", "TestDb"))),
                Node("insert", "builtin.database.execute", Props(
                    ("connectionId", "TestDb"),
                    ("command", "INSERT INTO users (id, name) VALUES (11, 'via-workflow')"))),
                Node("committedCount", "builtin.database.query", Props(
                    ("connectionId", "TestDb"),
                    ("query", "SELECT id FROM users WHERE id = 11"))),
                Connections(
                    Conn("txn", "transactionBody", "insert"),
                    Conn("txn", "committed", "committedCount")));

            var completed = RunWorkflow(definition, services);

            completed.Outputs["committedCount.rowCount"].Should().Be(1);
            CountUsers(services, "TestDb").Should().Be(1);
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public void TransactionBody_NodeFails_RollsBackAndRoutesRolledBack()
    {
        var dbPath = NewDbPath();
        try
        {
            var services = BuildServices(("TestDb", dbPath));
            Seed(services, "TestDb");

            var definition = Workflow(
                Node("insert", "builtin.database.execute", Props(
                    ("connectionId", "TestDb"),
                    ("command", "INSERT INTO users (id, name) VALUES (20, 'rolled')"))),
                Node("fail", "builtin.database.execute", Props(
                    ("connectionId", "TestDb"),
                    ("command", "INSERT INTO missing_table (id) VALUES (1)"))),
                Connections(
                    Conn("txn", "transactionBody", "insert"),
                    Conn("insert", "success", "fail")));

            var completed = RunTransaction(definition, services, "TestDb");

            CountUsers(services, "TestDb").Should().Be(0, "the earlier body insert must roll back~ 🛡️");
            completed.Outputs["success"].Should().Be(false);
            completed.Outputs["rolledBack"].Should().Be(true);
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public void TransactionBody_SameConnectionEnlistsDifferentConnectionDoesNot()
    {
        var mainPath = NewDbPath();
        var auditPath = NewDbPath();
        try
        {
            var services = BuildServices(("MainDb", mainPath), ("AuditDb", auditPath));
            Seed(services, "MainDb");
            Seed(services, "AuditDb");

            var definition = Workflow(
                Node("mainInsert", "builtin.database.execute", Props(
                    ("connectionId", "MainDb"),
                    ("command", "INSERT INTO users (id, name) VALUES (30, 'ambient')"))),
                Node("auditInsert", "builtin.database.execute", Props(
                    ("connectionId", "AuditDb"),
                    ("command", "INSERT INTO users (id, name) VALUES (40, 'outside')"))),
                Node("fail", "builtin.database.execute", Props(
                    ("connectionId", "MainDb"),
                    ("command", "INSERT INTO missing_table (id) VALUES (1)"))),
                Connections(
                    Conn("txn", "transactionBody", "mainInsert"),
                    Conn("mainInsert", "success", "auditInsert"),
                    Conn("auditInsert", "success", "fail")));

            var completed = RunTransaction(definition, services, "MainDb");

            completed.Outputs["rolledBack"].Should().Be(true);
            CountUsers(services, "MainDb").Should().Be(0, "same connectionId should enlist and roll back~ 💼");
            CountUsers(services, "AuditDb").Should().Be(1, "different connectionId should use its own connection and commit normally~ 🌸");
        }
        finally
        {
            DeleteDb(mainPath);
            DeleteDb(auditPath);
        }
    }

    private TransactionCompleted RunTransaction(WorkflowDefinition definition, IServiceProvider services, string connectionId)
    {
        var actor = Sys.ActorOf(Akka.Actor.Props.Create(() => new TransactionHarnessActor(TestActor, definition, services, connectionId)));
        actor.Tell("start");
        ExpectMsg("started", TimeSpan.FromSeconds(3));
        return FishForMessage(
            message => message is TransactionCompleted or TransactionFailed or Terminated,
            TimeSpan.FromSeconds(15)) switch
        {
            TransactionCompleted completed => completed,
            TransactionFailed failed => throw failed.Error,
            Terminated terminated => throw new InvalidOperationException($"Workflow actor terminated: {terminated.ActorRef.Path}"),
            var other => throw new InvalidOperationException($"Unexpected workflow terminal message: {other}"),
        };
    }

    private WorkflowCompleted RunWorkflow(WorkflowDefinition definition, IServiceProvider services)
    {
        var actor = Sys.ActorOf(Akka.Actor.Props.Create(() => new WorkflowHarnessActor(TestActor, definition, services)));
        actor.Tell("start");
        ExpectMsg("started", TimeSpan.FromSeconds(3));
        return FishForMessage(
            message => message is WorkflowCompleted or WorkflowFailed or Terminated,
            TimeSpan.FromSeconds(15)) switch
        {
            WorkflowCompleted completed => completed,
            WorkflowFailed failed => throw failed.Error,
            Terminated terminated => throw new InvalidOperationException($"Workflow actor terminated: {terminated.ActorRef.Path}"),
            var other => throw new InvalidOperationException($"Unexpected workflow terminal message: {other}"),
        };
    }

    private sealed class WorkflowHarnessActor : ReceiveActor
    {
        private readonly IActorRef probe;
        private readonly WorkflowDefinition definition;
        private readonly IServiceProvider services;

        public WorkflowHarnessActor(IActorRef probe, WorkflowDefinition definition, IServiceProvider services)
        {
            this.probe = probe;
            this.definition = definition;
            this.services = services;

            Receive<string>(msg => msg == "start", _ => this.StartWorkflow());
            Receive<WorkflowCompleted>(m => this.probe.Forward(m));
            Receive<WorkflowFailed>(m => this.probe.Forward(m));
            ReceiveAny(m => this.probe.Forward(m));
        }

        private void StartWorkflow()
        {
            var executionId = Guid.NewGuid();
            var child = Context.ActorOf(WorkflowExecutor.Props(
                executionId,
                this.definition,
                new Dictionary<string, object?>(),
                this.services));
            Context.Watch(child);
            this.probe.Tell("started");
            child.Tell(new StartExecution(executionId));
        }
    }

    private sealed class TransactionHarnessActor : ReceiveActor
    {
        private readonly IActorRef probe;
        private readonly WorkflowDefinition definition;
        private readonly IServiceProvider services;
        private readonly string connectionId;

        public TransactionHarnessActor(IActorRef probe, WorkflowDefinition definition, IServiceProvider services, string connectionId)
        {
            this.probe = probe;
            this.definition = definition;
            this.services = services;
            this.connectionId = connectionId;

            Receive<string>(msg => msg == "start", _ => this.StartWorkflow());
            Receive<TransactionCompleted>(m => this.probe.Forward(m));
            Receive<TransactionFailed>(m => this.probe.Forward(m));
            ReceiveAny(m => this.probe.Forward(m));
        }

        private void StartWorkflow()
        {
            var executionId = Guid.NewGuid();
            var child = Context.ActorOf(TransactionExecutorActor.Props(
                "txn",
                new TransactionRequest { ConnectionId = this.connectionId },
                this.definition,
                executionId,
                this.services));
            Context.Watch(child);
            this.probe.Tell("started");
        }
    }

    private static IServiceProvider BuildServices(params (string Id, string Path)[] connections)
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new DatabaseTransactionModule());
        registry.RegisterModule(new DatabaseExecuteModule());
        registry.RegisterModule(new DatabaseQueryModule());
        registry.RegisterModule(new DatabaseBulkInsertModule());

        var services = new ServiceCollection();
        services.AddSingleton<IModuleRegistry>(registry);
        services.Configure<DatabaseConnectionsOptions>(options =>
        {
            foreach (var (id, path) in connections)
            {
                options.Connections[id] = new DbConnectionDescriptor(id, "sqlite", $"Data Source={path};Pooling=False");
            }
        });
        services.AddDatabaseModules();
        return services.BuildServiceProvider();
    }

    private static WorkflowDefinition Workflow(params object[] parts)
    {
        var nodes = new List<NodeDefinition>();
        var connections = new List<ConnectionDefinition>();
        foreach (var part in parts)
        {
            if (part is NodeDefinition n) nodes.Add(n);
            if (part is IEnumerable<ConnectionDefinition> cs) connections.AddRange(cs);
        }

        return new WorkflowDefinition(
            Guid.NewGuid(),
            "Structural transaction test",
            null,
            new Version(1, 0, 0),
            nodes.ToArr(),
            connections.ToArr(),
            HashMap<string, VariableDefinition>.Empty);
    }

    private static NodeDefinition Node(string id, string moduleId, HashMap<string, JsonElement> props)
        => new(id, moduleId, id, props);

    private static ConnectionDefinition Conn(string source, string port, string target)
        => new(source, port, target, "input");

    private static IEnumerable<ConnectionDefinition> Connections(params ConnectionDefinition[] connections) => connections;

    private static HashMap<string, JsonElement> Props(params (string Key, object? Value)[] values)
    {
        var dict = new Dictionary<string, JsonElement>();
        foreach (var (key, value) in values)
        {
            dict[key] = JsonSerializer.SerializeToElement(value, value?.GetType() ?? typeof(object));
        }

        return dict.ToHashMap();
    }

    private static string NewDbPath()
    {
        var dir = Path.Combine(Directory.GetCurrentDirectory(), "TestArtifacts");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"dotflow-txn-{Guid.NewGuid():N}.db");
    }

    private static void Seed(IServiceProvider services, string connectionId)
    {
        var factory = services.GetRequiredService<IDbConnectionFactory>();
        using DataConnection db = factory.CreateAsync(connectionId).AsTask().GetAwaiter().GetResult();
        db.Execute("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");
    }

    private static int CountUsers(IServiceProvider services, string connectionId)
    {
        var factory = services.GetRequiredService<IDbConnectionFactory>();
        using DataConnection db = factory.CreateAsync(connectionId).AsTask().GetAwaiter().GetResult();
        return db.Execute<int>("SELECT COUNT(*) FROM users");
    }

    private static void DeleteDb(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
