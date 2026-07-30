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

    /// <inheritdoc/>
    public async Task<LinqPreviewResult> PreviewLiveAsync(LinqLivePreviewRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var compile = await this.compiler.CompileAsync(request.Compile, ct).ConfigureAwait(false);
        if (!compile.Success || compile.AssemblyBytes is null)
        {
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

        return await RunLiveInAlcAsync(
            compile.AssemblyBytes,
            request.ProviderName,
            request.ConnectionString,
            request.Inputs ?? new Dictionary<string, object?>(),
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

            var capture = new SqlCapture();
            var options = capture.Configure(new DataOptions()
                .UseConnectionString(ProviderName.SQLiteMS, "Data Source=:memory:"));
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

                // Capture only the user body's SQL — table creation/seeding is scaffolding~ 🧾
                capture.Attach(db);

                var sw = Stopwatch.StartNew();
                LinqExecutionResult materialized;
                var extraWarnings = new List<LinqDiagnostic>(warnings);

                // ── Always-rollback wrapper (§8.5) — user side effects never persist ──────────
                db.BeginTransaction();
                try
                {
                    var raw = await InvokeBodyAsync(scriptType, inputsType, db, inputs, ct).ConfigureAwait(false);
                    materialized = MaterializeOrRenderSql(raw, capture, extraWarnings);
                }
                finally
                {
                    db.RollbackTransaction();
                    capture.Detach(db);
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
                    extraWarnings,
                    seeded,
                    postRollbackCount,
                    capture.Statements);
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

    /// <summary>
    /// ⚠️ Runs the body against the real connection. Everything happens inside a transaction that is
    /// rolled back unconditionally, so writes never persist — but the query does hit real data~ 🗄️.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<LinqPreviewResult> RunLiveInAlcAsync(
        byte[] assemblyBytes,
        string providerName,
        string connectionString,
        IReadOnlyDictionary<string, object?> inputs,
        IReadOnlyList<LinqDiagnostic> warnings,
        CancellationToken ct)
    {
        var alc = new AssemblyLoadContext($"linq-live-{Guid.NewGuid():N}", isCollectible: true);
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

            var capture = new SqlCapture();
            var options = capture.Configure(new DataOptions().UseConnectionString(providerName, connectionString));
            var db = (DataConnection)Activator.CreateInstance(contextType, options)!;
            try
            {
                capture.Attach(db);
                var sw = Stopwatch.StartNew();
                LinqExecutionResult materialized;
                var diagnostics = new List<LinqDiagnostic>(warnings);

                db.BeginTransaction();
                try
                {
                    var raw = await InvokeBodyAsync(scriptType, inputsType, db, inputs, ct).ConfigureAwait(false);
                    materialized = MaterializeOrRenderSql(raw, capture, diagnostics);
                }
                finally
                {
                    // Unconditional rollback — the whole point of a live preview~ 🔒
                    db.RollbackTransaction();
                    capture.Detach(db);
                }

                sw.Stop();
                diagnostics.Add(new LinqDiagnostic(
                    "WFLINQ020",
                    LinqDiagnosticSeverity.Warning,
                    "Ran against the live connection inside a transaction that was rolled back — no changes were persisted~ 🔒"));

                return new LinqPreviewResult(
                    true,
                    materialized.Rows,
                    materialized.Result,
                    materialized.RowCount,
                    sw.ElapsedMilliseconds,
                    diagnostics,
                    0,
                    null,
                    capture.Statements);
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

    private static async Task<object?> InvokeBodyAsync(
        Type scriptType,
        Type inputsType,
        DataConnection db,
        IReadOnlyDictionary<string, object?> inputs,
        CancellationToken ct)
    {
        var inputsObj = Activator.CreateInstance(inputsType, inputs)!;
        var script = Activator.CreateInstance(scriptType)!;
        var method = scriptType.GetMethod("ExecuteAsync")!;
        var task = (Task)method.Invoke(script, new[] { db, inputsObj, ct })!;
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")!.GetValue(task);
    }

    /// <summary>
    /// Materialises the result — or, when the body returned an unmaterialised <c>IQueryable</c>,
    /// renders its SQL via <c>ToSqlQuery()</c> instead of failing, with a "add .ToList()" hint~ 🧾.
    /// </summary>
    private static LinqExecutionResult MaterializeOrRenderSql(
        object? raw,
        SqlCapture capture,
        List<LinqDiagnostic> diagnostics)
    {
        if (raw is IQueryable queryable)
        {
            var sql = TryRenderSql(queryable);
            if (sql is not null)
            {
                capture.Add(sql);
            }

            diagnostics.Add(new LinqDiagnostic(
                "WFLINQ021",
                LinqDiagnosticSeverity.Warning,
                "The body returned a query without materialising it — the SQL is shown below. "
                + "Add '.ToList()' (or .First()/.Count()/…) to actually run it and see rows~ 🧾"));

            return new LinqExecutionResult { Result = null };
        }

        return LinqResultMaterializer.Materialize(raw);
    }

    private static string? TryRenderSql(IQueryable queryable)
    {
        try
        {
            var method = typeof(LinqExtensions)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "ToSqlQuery"
                    && m.IsGenericMethodDefinition
                    && m.GetGenericArguments().Length == 1
                    && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(IQueryable<>))
                .MakeGenericMethod(queryable.ElementType);

            var querySql = method.Invoke(null, new object?[] { queryable, null });
            return querySql?.GetType().GetProperty("Sql")?.GetValue(querySql) as string;
        }
        catch (Exception ex) when (ex is TargetInvocationException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>🧾 Collects the SQL linq2db executes on a connection (tracing seam)~.</summary>
    private sealed class SqlCapture
    {
        private readonly List<string> statements = new();

        public IReadOnlyList<string> Statements => this.statements;

        /// <summary>Gets or sets a value indicating whether traced statements are recorded.</summary>
        public bool Enabled { get; set; }

        /// <summary>Applies the tracing hook to the options (must be set before the connection exists)~.</summary>
        public DataOptions Configure(DataOptions options)
            => options.UseTracing(System.Diagnostics.TraceLevel.Info, this.OnTrace);

        public void Attach(DataConnection db)
        {
            db.OnTraceConnection = this.OnTrace;
            this.Enabled = true;
        }

        public void Detach(DataConnection db) => this.Enabled = false;

        public void Add(string sql)
        {
            var trimmed = sql.Trim();
            if (trimmed.Length > 0 && !this.statements.Contains(trimmed, StringComparer.Ordinal))
            {
                this.statements.Add(trimmed);
            }
        }

        private void OnTrace(TraceInfo info)
        {
            // Only real commands — transaction bookkeeping traces carry no SQL of interest~
            if (!this.Enabled || info.TraceInfoStep != TraceInfoStep.BeforeExecute || info.Command is null)
            {
                return;
            }

            var sql = info.SqlText ?? info.CommandText;
            if (!string.IsNullOrWhiteSpace(sql))
            {
                this.Add(sql!);
            }
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

