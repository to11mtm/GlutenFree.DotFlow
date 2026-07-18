// <copyright file="CollectibleScriptRunner.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq.Execution;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;

/// <summary>
/// 🚀 Loads compiled linq assemblies into collectible ALCs and runs the generated
/// <c>WorkflowScript.ExecuteAsync</c>, materialising results out of the ALC (2.4.b.3 + 2.4.b.6)~ ✨.
/// </summary>
public interface ILinqScriptRunner
{
    /// <summary>
    /// Runs the compiled body against the given connection options + inputs, returning ALC-free rows~ 🎯.
    /// </summary>
    /// <param name="assemblyKey">Stable cache key for the compiled assembly (the blob key).</param>
    /// <param name="assemblyBytes">The verified compiled assembly bytes.</param>
    /// <param name="options">linq2db options for the target connection.</param>
    /// <param name="inputs">The node input values (wrapped into the codegen'd <c>LinqInputs</c>).</param>
    /// <param name="timeoutSeconds">Command timeout in seconds.</param>
    /// <param name="ct">Cancellation token (flows into the user body).</param>
    /// <returns>The materialised result.</returns>
    Task<LinqExecutionResult> RunAsync(
        string assemblyKey,
        byte[] assemblyBytes,
        DataOptions options,
        IReadOnlyDictionary<string, object?> inputs,
        int timeoutSeconds,
        CancellationToken ct);
}

/// <summary>
/// 🚀 Collectible-ALC runner that <b>reuses</b> a loaded assembly per key (2.4.b.6)~ 💖.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote (ALC-leak-under-load fix): the 2.4.b.3 "unload after every execution" design leaked
/// under load — linq2db caches compiled query delegates by entity type, transiently rooting each
/// per-execution ALC (design §8.4.4). Instead we load ONE collectible ALC per compiled-assembly key
/// and reuse it across executions (fresh <c>DataConnection</c> per call). ALC count is bounded by the
/// LRU capacity (distinct assemblies), NOT by execution count — so 1000 executions of one assembly use
/// exactly one ALC. Evicted/disposed ALCs are unloaded~ 🌸.
/// </para>
/// </remarks>
public sealed class CollectibleScriptRunner : ILinqScriptRunner, IDisposable
{
    private const string RuntimeNamespace = "WorkflowRuntime";

    private readonly int capacity;
    private readonly object gate = new();
    private readonly Dictionary<string, LoadedScript> cache = new(StringComparer.Ordinal);
    private readonly LinkedList<string> lru = new();
    private bool disposed;

    /// <summary>Initializes a new instance of the <see cref="CollectibleScriptRunner"/> class~ 🚀.</summary>
    /// <param name="loadedAssemblyCapacity">Max distinct compiled assemblies kept loaded (default 64).</param>
    public CollectibleScriptRunner(int loadedAssemblyCapacity = 64)
    {
        this.capacity = Math.Max(1, loadedAssemblyCapacity);
    }

    /// <summary>Gets the number of currently-loaded compiled assemblies (for leak assertions)~ 🔍.</summary>
    public int LoadedAssemblyCount
    {
        get
        {
            lock (this.gate)
            {
                return this.cache.Count;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<LinqExecutionResult> RunAsync(
        string assemblyKey,
        byte[] assemblyBytes,
        DataOptions options,
        IReadOnlyDictionary<string, object?> inputs,
        int timeoutSeconds,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyKey);
        ArgumentNullException.ThrowIfNull(assemblyBytes);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(inputs);

        var loaded = this.GetOrLoad(assemblyKey, assemblyBytes);

        // Fresh DataConnection per execution (isolated); the ALC + types are reused~
        var db = (DataConnection)Activator.CreateInstance(loaded.ContextType, options)!;
        try
        {
            db.CommandTimeout = timeoutSeconds;

            var inputsObj = Activator.CreateInstance(loaded.InputsType, inputs)!;
            var script = Activator.CreateInstance(loaded.ScriptType)!;
            var task = (Task)loaded.ExecuteMethod.Invoke(script, new[] { db, inputsObj, ct })!;
            await task.ConfigureAwait(false);

            var raw = task.GetType().GetProperty("Result")!.GetValue(task);

            // Copy everything out of the ALC into BCL types (D8 / §8.4)~ 📤
            var materialized = LinqResultMaterializer.Materialize(raw);
            return new LinqExecutionResult
            {
                Rows = materialized.Rows,
                Result = materialized.Result,
                RowCount = materialized.RowCount,
                AlcWeakRef = loaded.Weak,
            };
        }
        finally
        {
            db.Dispose();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (this.gate)
        {
            if (this.disposed)
            {
                return;
            }

            foreach (var loaded in this.cache.Values)
            {
                loaded.Alc.Unload();
            }

            this.cache.Clear();
            this.lru.Clear();
            this.disposed = true;
        }
    }

    private LoadedScript GetOrLoad(string key, byte[] bytes)
    {
        lock (this.gate)
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);

            if (this.cache.TryGetValue(key, out var existing))
            {
                this.lru.Remove(key);
                this.lru.AddFirst(key);
                return existing;
            }

            var alc = new AssemblyLoadContext($"linq-{Guid.NewGuid():N}", isCollectible: true);
            Assembly assembly;
            using (var ms = new MemoryStream(bytes, writable: false))
            {
                assembly = alc.LoadFromStream(ms);
            }

            var contextType = assembly.GetType($"{RuntimeNamespace}.DynamicWorkflowContext")
                ?? throw new InvalidOperationException("Compiled assembly is missing DynamicWorkflowContext~ 💔");
            var inputsType = assembly.GetType($"{RuntimeNamespace}.LinqInputs")
                ?? throw new InvalidOperationException("Compiled assembly is missing LinqInputs~ 💔");
            var scriptType = assembly.GetType($"{RuntimeNamespace}.WorkflowScript")
                ?? throw new InvalidOperationException("Compiled assembly is missing WorkflowScript~ 💔");
            var executeMethod = scriptType.GetMethod("ExecuteAsync")
                ?? throw new InvalidOperationException("WorkflowScript is missing ExecuteAsync~ 💔");

            var loaded = new LoadedScript(alc, contextType, inputsType, scriptType, executeMethod, new WeakReference(alc));
            this.cache[key] = loaded;
            this.lru.AddFirst(key);

            while (this.cache.Count > this.capacity && this.lru.Last is { } last)
            {
                this.lru.RemoveLast();
                if (this.cache.Remove(last.Value, out var evicted))
                {
                    evicted.Alc.Unload();
                }
            }

            return loaded;
        }
    }

    private sealed record LoadedScript(
        AssemblyLoadContext Alc,
        Type ContextType,
        Type InputsType,
        Type ScriptType,
        MethodInfo ExecuteMethod,
        WeakReference Weak);
}


