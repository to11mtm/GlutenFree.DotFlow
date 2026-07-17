// <copyright file="CollectibleScriptRunner.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq.Execution;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;

/// <summary>
/// 🚀 Loads a compiled linq assembly into a collectible ALC, runs the generated
/// <c>WorkflowScript.ExecuteAsync</c>, materialises the result, then unloads (2.4.b.3)~ ✨.
/// </summary>
public interface ILinqScriptRunner
{
    /// <summary>
    /// Runs the compiled body against the given connection options + inputs, returning ALC-free rows~ 🎯.
    /// </summary>
    /// <param name="assemblyBytes">The verified compiled assembly bytes.</param>
    /// <param name="options">linq2db options for the target connection.</param>
    /// <param name="inputs">The node input values (wrapped into the codegen'd <c>LinqInputs</c>).</param>
    /// <param name="timeoutSeconds">Command timeout in seconds.</param>
    /// <param name="ct">Cancellation token (flows into the user body).</param>
    /// <returns>The materialised result.</returns>
    Task<LinqExecutionResult> RunAsync(
        byte[] assemblyBytes,
        DataOptions options,
        IReadOnlyDictionary<string, object?> inputs,
        int timeoutSeconds,
        CancellationToken ct);
}

/// <summary>
/// 🚀 Default collectible-ALC runner. The emitted assembly is the ONLY thing loaded into the ALC;
/// linq2db + the BCL resolve from the default context so <c>DataConnection</c>/<c>DataOptions</c>
/// keep type identity (§8.4 invariants)~ 💖.
/// </summary>
public sealed class CollectibleScriptRunner : ILinqScriptRunner
{
    private const string RuntimeNamespace = "WorkflowRuntime";

    /// <inheritdoc/>
    public async Task<LinqExecutionResult> RunAsync(
        byte[] assemblyBytes,
        DataOptions options,
        IReadOnlyDictionary<string, object?> inputs,
        int timeoutSeconds,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(assemblyBytes);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(inputs);

        // The load+run happens in a NoInlining helper so no ALC-typed locals linger in this frame
        // once it returns — a prerequisite for the ALC being collectible right after Unload~ 🧹
        return await RunInAlcAsync(assemblyBytes, options, inputs, timeoutSeconds, ct).ConfigureAwait(false);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<LinqExecutionResult> RunInAlcAsync(
        byte[] assemblyBytes,
        DataOptions options,
        IReadOnlyDictionary<string, object?> inputs,
        int timeoutSeconds,
        CancellationToken ct)
    {
        var alc = new AssemblyLoadContext($"linq-exec-{Guid.NewGuid():N}", isCollectible: true);
        var weak = new WeakReference(alc);

        try
        {
            Assembly assembly;
            using (var ms = new MemoryStream(assemblyBytes, writable: false))
            {
                assembly = alc.LoadFromStream(ms);
            }

            var contextType = assembly.GetType($"{RuntimeNamespace}.DynamicWorkflowContext")
                ?? throw new InvalidOperationException("Compiled assembly is missing DynamicWorkflowContext~ 💔");
            var inputsType = assembly.GetType($"{RuntimeNamespace}.LinqInputs")
                ?? throw new InvalidOperationException("Compiled assembly is missing LinqInputs~ 💔");
            var scriptType = assembly.GetType($"{RuntimeNamespace}.WorkflowScript")
                ?? throw new InvalidOperationException("Compiled assembly is missing WorkflowScript~ 💔");

            var db = (DataConnection)Activator.CreateInstance(contextType, options)!;
            try
            {
                db.CommandTimeout = timeoutSeconds;

                var inputsObj = Activator.CreateInstance(inputsType, inputs)!;
                var script = Activator.CreateInstance(scriptType)!;
                var method = scriptType.GetMethod("ExecuteAsync")
                    ?? throw new InvalidOperationException("WorkflowScript is missing ExecuteAsync~ 💔");

                var task = (Task)method.Invoke(script, new[] { db, inputsObj, ct })!;
                await task.ConfigureAwait(false);

                var raw = task.GetType().GetProperty("Result")!.GetValue(task);

                // Copy everything out of the ALC into BCL types BEFORE we dispose + unload~ 📤
                var materialized = LinqResultMaterializer.Materialize(raw);
                return new LinqExecutionResult
                {
                    Rows = materialized.Rows,
                    Result = materialized.Result,
                    RowCount = materialized.RowCount,
                    AlcWeakRef = weak,
                };
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
}

