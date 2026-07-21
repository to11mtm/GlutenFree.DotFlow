// <copyright file="WorkflowLinqPreviewer.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq.Preview;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Modules.Database.Linq.Abstractions;
using Workflow.Modules.Database.Linq.Execution;

/// <summary>
/// 🔎 Compiles + previews a typed linq body in a rollback-only in-memory SQLite sandbox (2.4.b.4)~ ✨💖.
/// </summary>
public sealed class WorkflowLinqPreviewer : IWorkflowLinqPreviewer
{
    private const string RuntimeNamespace = "WorkflowRuntime";

    private readonly IWorkflowLinqCompiler compiler;
    private readonly ILogger<WorkflowLinqPreviewer> logger;

    /// <summary>Initializes a new instance of the <see cref="WorkflowLinqPreviewer"/> class~ 🔎.</summary>
    /// <param name="compiler">The linq compiler.</param>
    /// <param name="logger">Logger (optional).</param>
    public WorkflowLinqPreviewer(IWorkflowLinqCompiler compiler, ILogger<WorkflowLinqPreviewer>? logger = null)
    {
        this.compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        this.logger = logger ?? NullLogger<WorkflowLinqPreviewer>.Instance;
    }

    /// <inheritdoc/>
    public async Task<LinqPreviewResult> PreviewAsync(LinqPreviewRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var compile = await this.compiler.CompileAsync(request.Compile, ct).ConfigureAwait(false);
        if (!compile.Success || compile.AssemblyBytes is null)
        {
            // Compile errors are returned as a clean, non-throwing result~ 🌸
            return new LinqPreviewResult(
                false,
                null,
                null,
                null,
                0,
                compile.Errors.Concat(compile.Warnings).ToList(),
                0,
                null);
        }

        var inputs = request.Inputs ?? new Dictionary<string, object?>();
        return await RunPreviewInAlcAsync(
            compile.AssemblyBytes,
            inputs,
            Math.Max(0, request.SampleRowsPerTable),
            compile.Warnings,
            ct).ConfigureAwait(false);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<LinqPreviewResult> RunPreviewInAlcAsync(
        byte[] assemblyBytes,
        IReadOnlyDictionary<string, object?> inputs,
        int sampleRowsPerTable,
        IReadOnlyList<LinqDiagnostic> warnings,
        CancellationToken ct)
    {
        var alc = new AssemblyLoadContext($"linq-preview-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            Assembly assembly;
            using (var ms = new MemoryStream(assemblyBytes, writable: false))
            {
                assembly = alc.LoadFromStream(ms);
            }

            var contextType = assembly.GetType($"{RuntimeNamespace}.DynamicWorkflowContext")!;
            var inputsType = assembly.GetType($"{RuntimeNamespace}.LinqInputs")!;
            var scriptType = assembly.GetType($"{RuntimeNamespace}.WorkflowScript")!;

            var options = new DataOptions().UseConnectionString(ProviderName.SQLiteMS, "Data Source=:memory:");
            var db = (DataConnection)Activator.CreateInstance(contextType, options)!;
            try
            {
                // Discover the generated POCO types from the context's ITable<T> properties~ 🧩
                var pocoTypes = contextType
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(ITable<>))
                    .Select(p => p.PropertyType.GetGenericArguments()[0])
                    .Distinct()
                    .ToList();

                var seeded = 0;
                foreach (var pocoType in pocoTypes)
                {
                    CreateTable(db, pocoType);
                    seeded += SeedTable(db, pocoType, sampleRowsPerTable);
                }

                var sw = Stopwatch.StartNew();
                LinqExecutionResult materialized;

                // ── Always-rollback wrapper (§8.5) — user side effects never persist ──────────
                db.BeginTransaction();
                try
                {
                    var inputsObj = Activator.CreateInstance(inputsType, inputs)!;
                    var script = Activator.CreateInstance(scriptType)!;
                    var method = scriptType.GetMethod("ExecuteAsync")!;
                    var task = (Task)method.Invoke(script, new[] { db, inputsObj, ct })!;
                    await task.ConfigureAwait(false);
                    var raw = task.GetType().GetProperty("Result")!.GetValue(task);
                    materialized = LinqResultMaterializer.Materialize(raw);
                }
                finally
                {
                    db.RollbackTransaction();
                }

                sw.Stop();

                // Seeds were committed BEFORE the txn; a correct rollback leaves them intact~ 🔒
                int? postRollbackCount = pocoTypes.Count > 0 ? CountTable(db, pocoTypes[0]) : null;

                return new LinqPreviewResult(
                    true,
                    materialized.Rows,
                    materialized.Result,
                    materialized.RowCount,
                    sw.ElapsedMilliseconds,
                    warnings,
                    seeded,
                    postRollbackCount);
            }
            finally
            {
                db.Dispose();
            }
        }
        finally
        {
            alc.Unload();
        }
    }

    private static void CreateTable(DataConnection db, Type pocoType)
    {
        var method = GenericDataExtension("CreateTable", minParams: 1, entityIsSecondArg: false)
            .MakeGenericMethod(pocoType);
        method.Invoke(null, BuildArgs(method, db, entity: null));
    }

    private static int SeedTable(DataConnection db, Type pocoType, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        var insert = GenericDataExtension("Insert", minParams: 2, entityIsSecondArg: true)
            .MakeGenericMethod(pocoType);

        var writableProps = pocoType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanWrite: true } && p.GetIndexParameters().Length == 0)
            .ToList();

        for (var i = 0; i < count; i++)
        {
            var entity = Activator.CreateInstance(pocoType)!;
            foreach (var prop in writableProps)
            {
                prop.SetValue(entity, SampleDataGenerator.For(prop.PropertyType, i));
            }

            insert.Invoke(null, BuildArgs(insert, db, entity));
        }

        return count;
    }

    private static int CountTable(DataConnection db, Type pocoType)
    {
        var getTable = GenericDataExtension("GetTable", minParams: 1, entityIsSecondArg: false)
            .MakeGenericMethod(pocoType);
        var table = (IEnumerable)getTable.Invoke(null, new object[] { db })!;

        var count = 0;
        foreach (var _ in table)
        {
            count++;
        }

        return count;
    }

    // Finds a generic DataExtensions method, filtering by the entity-arg shape to dodge overload ambiguity.
    private static MethodInfo GenericDataExtension(string name, int minParams, bool entityIsSecondArg)
        => typeof(DataExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m =>
                m.Name == name
                && m.IsGenericMethodDefinition
                && m.GetGenericArguments().Length == 1
                && m.GetParameters().Length >= minParams
                && typeof(IDataContext).IsAssignableFrom(m.GetParameters()[0].ParameterType)
                && (!entityIsSecondArg || m.GetParameters()[1].ParameterType == m.GetGenericArguments()[0]));

    // Builds the invoke args, filling optional trailing params with Type.Missing.
    private static object[] BuildArgs(MethodInfo method, DataConnection db, object? entity)
    {
        var pars = method.GetParameters();
        var args = new object[pars.Length];
        args[0] = db;

        var start = 1;
        if (entity is not null)
        {
            args[1] = entity;
            start = 2;
        }

        for (var i = start; i < args.Length; i++)
        {
            args[i] = Type.Missing;
        }

        return args;
    }
}

