// <copyright file="ScopeStep.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Models;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

/// <summary>
/// One construct a body node runs inside: a loop iteration, a parallel branch, a try/catch branch or
/// a transaction body. A body's scope path is the list of these from the outermost construct inwards~ 🧭.
/// </summary>
/// <remarks>
/// §12.10 follow-up: since constructs can nest, <c>Metadata["subGraphId"]</c> only names the innermost
/// body ("inner-iter-2"). <see cref="Actors.SubGraphExecutor"/> stamps the full path on every body
/// history record as <c>Metadata["scopePath"]</c>, and the innermost loop step as
/// <c>LoopId</c> / <c>LoopIteration</c>.
/// </remarks>
/// <param name="NodeId">The construct node.</param>
/// <param name="Kind">One of <see cref="Loop"/>, <see cref="Parallel"/>, <see cref="TryCatch"/>, <see cref="Transaction"/>.</param>
/// <param name="Iteration">Loop only: the 1-based iteration.</param>
/// <param name="BranchIndex">Parallel only: the branch index.</param>
/// <param name="Branch">Try/catch only: <c>try</c>, <c>catch</c> or <c>finally</c>.</param>
public sealed record ScopeStep(
    string NodeId,
    string Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Iteration = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? BranchIndex = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Branch = null)
{
    /// <summary>Kind of a loop iteration step.</summary>
    public const string Loop = "loop";

    /// <summary>Kind of a parallel branch step.</summary>
    public const string Parallel = "parallel";

    /// <summary>Kind of a try/catch branch step.</summary>
    public const string TryCatch = "trycatch";

    /// <summary>Kind of a transaction body step.</summary>
    public const string Transaction = "transaction";

    /// <summary>The path of a top-level construct's bodies' parent: empty.</summary>
    public static readonly IReadOnlyList<ScopeStep> EmptyPath = System.Array.Empty<ScopeStep>();

    /// <summary>Returns <paramref name="parentPath"/> with <paramref name="step"/> appended (a new list).</summary>
    public static IReadOnlyList<ScopeStep> Append(IReadOnlyList<ScopeStep>? parentPath, ScopeStep step)
        => (parentPath ?? EmptyPath).Append(step).ToArray();

    /// <summary>Short form for logs and tests: <c>loop#2</c>, <c>par[1]</c>, <c>tc:catch</c>, <c>txn</c>.</summary>
    public override string ToString() => Kind switch
    {
        Loop => $"{NodeId}#{Iteration}",
        Parallel => $"{NodeId}[{BranchIndex}]",
        TryCatch => $"{NodeId}:{Branch}",
        _ => NodeId,
    };
}
