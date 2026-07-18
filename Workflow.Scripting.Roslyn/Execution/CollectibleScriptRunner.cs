// <copyright file="CollectibleScriptRunner.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Roslyn.Execution;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 🚀 Loads compiled script assemblies into collectible ALCs and invokes a static entry method by
/// convention, reusing one ALC per assembly key (bounded by LRU capacity)~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: Domain-agnostic generalisation of the 2.4.b linq runner. One collectible ALC is
/// loaded per compiled-assembly key and reused across executions — ALC count is bounded by distinct
/// assemblies (LRU), NOT execution count. Result materialisation is the caller's responsibility~ 🌸.
/// </remarks>
public sealed class CollectibleScriptRunner : IDisposable
{
    private readonly int capacity;
    private readonly object gate = new();
    private readonly Dictionary<string, Loaded> cache = new(StringComparer.Ordinal);
    private readonly LinkedList<string> lru = new();
    private bool disposed;

    /// <summary>Initializes a new instance of the <see cref="CollectibleScriptRunner"/> class~ 🚀.</summary>
    /// <param name="loadedAssemblyCapacity">Max distinct compiled assemblies kept loaded (default 64).</param>
    public CollectibleScriptRunner(int loadedAssemblyCapacity = 64)
    {
        this.capacity = Math.Max(1, loadedAssemblyCapacity);
    }

    /// <summary>Gets the number of currently-loaded assemblies (for leak assertions)~ 🔍.</summary>
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

    /// <summary>
    /// Invokes <c>{typeName}.{methodName}(args)</c> from the compiled assembly, awaiting the returned
    /// <see cref="Task"/> and returning its result~ 🎯.
    /// </summary>
    /// <param name="assemblyKey">Stable cache key for the compiled assembly.</param>
    /// <param name="assemblyBytes">The verified compiled assembly bytes.</param>
    /// <param name="typeName">The fully-qualified entry type name.</param>
    /// <param name="methodName">The static async entry method name.</param>
    /// <param name="args">The invocation arguments.</param>
    /// <returns>The awaited result (still ALC-typed until the caller materialises it).</returns>
    public async Task<object?> RunAsync(
        string assemblyKey,
        byte[] assemblyBytes,
        string typeName,
        string methodName,
        object?[] args)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyKey);
        ArgumentNullException.ThrowIfNull(assemblyBytes);

        var loaded = this.GetOrLoad(assemblyKey, assemblyBytes, typeName, methodName);
        var instance = loaded.Method.IsStatic ? null : Activator.CreateInstance(loaded.Type);
        var task = (Task)loaded.Method.Invoke(instance, args)!;
        await task.ConfigureAwait(false);

        var resultProp = task.GetType().GetProperty("Result");
        return resultProp?.GetValue(task);
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

    private Loaded GetOrLoad(string key, byte[] bytes, string typeName, string methodName)
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

            var alc = new AssemblyLoadContext($"script-{Guid.NewGuid():N}", isCollectible: true);
            Assembly assembly;
            using (var ms = new MemoryStream(bytes, writable: false))
            {
                assembly = alc.LoadFromStream(ms);
            }

            var type = assembly.GetType(typeName)
                ?? throw new InvalidOperationException($"Compiled assembly is missing {typeName}~ 💔");
            var method = type.GetMethod(methodName)
                ?? throw new InvalidOperationException($"{typeName} is missing {methodName}~ 💔");

            var loaded = new Loaded(alc, type, method);
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

    private sealed record Loaded(AssemblyLoadContext Alc, Type Type, MethodInfo Method);
}
