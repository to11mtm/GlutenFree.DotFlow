// <copyright file="LoopContext.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Models;

/// <summary>
///  Represents the mutable execution context for a single active loop scope~ ✨
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.2.0b — LoopContext is a <b>mutable class</b> (not a record) so
/// BreakModule / ContinueModule can set flags in-place without replacing the entire
/// stack entry. The stack is owned by WorkflowExecutor's <c>_loopContextStack</c>~ .
/// </para>
/// <para>
/// Nested loops are supported: WorkflowExecutor maintains a <c>Stack&lt;LoopContext&gt;</c>
/// and modules like BreakModule / ContinueModule target the top of the stack (innermost loop)~ .
/// </para>
/// <para>
/// Variable isolation: each iteration uses the prefix <c>loop:{LoopId}:{Iteration}</c>
/// (from <see cref="VariableScopePrefix"/>) so writes inside one iteration cannot be read
/// by a different iteration in the same scope. The LoopModule (2.2.2) is responsible for
/// namespacing variable reads/writes using this prefix~ .
/// </para>
/// </remarks>
public sealed class LoopContext
{
    /// <summary>Gets the unique identifier for this loop scope instance~ .</summary>
    public string LoopId { get; }

    /// <summary>Gets the current 1-based iteration number~ .</summary>
    public int Iteration { get; private set; }

    /// <summary>
    /// Gets or sets the current item being processed (for ForEach-style loops)~ .
    /// <see langword="null"/> for counted loops.
    /// </summary>
    public object? Item { get; set; }

    /// <summary>Gets the 0-based index of the current element within the loop collection~ .</summary>
    public int Index { get; private set; }

    /// <summary>
    /// Gets the optional parent variable scope prefix for variable inheritance~ .
    /// Used when nested loops need to read from an outer scope.
    /// </summary>
    public string? ParentScope { get; }

    /// <summary>Gets or sets whether a break has been requested for this loop~ .</summary>
    public bool BreakRequested { get; set; }

    /// <summary>Gets or sets whether a continue has been requested for this iteration~ ⏭️.</summary>
    public bool ContinueRequested { get; set; }

    /// <summary>
    /// Gets the variable scope prefix for this iteration~ .
    /// <br/>Format: <c>loop:{LoopId}:{Iteration}</c>
    /// <br/>Used to namespace per-iteration variable writes so they don't leak across iterations~ ✨.
    /// </summary>
    public string VariableScopePrefix => $"loop:{LoopId}:{Iteration}";

    /// <summary>
    /// Initializes a new instance of the <see cref="LoopContext"/> class.
    /// </summary>
    /// <param name="loopId">Unique ID for this loop scope (typically the loop node's ID)~ .</param>
    /// <param name="initialIteration">Starting iteration number (default 1, 1-based)~ .</param>
    /// <param name="parentScope">
    /// Optional parent variable scope prefix — used by nested loops to access outer-scope variables~ .
    /// </param>
    public LoopContext(string loopId, int initialIteration = 1, string? parentScope = null)
    {
        LoopId = loopId;
        Iteration = initialIteration;
        Index = 0;
        ParentScope = parentScope;
    }

    /// <summary>
    /// Advances to the next iteration: increments <see cref="Iteration"/>, increments <see cref="Index"/>,
    /// and resets the <see cref="BreakRequested"/> and <see cref="ContinueRequested"/> flags~ ➕.
    /// </summary>
    public void AdvanceIteration()
    {
        Iteration++;
        Index++;
        BreakRequested = false;
        ContinueRequested = false;
    }

    /// <summary>
    /// Sets the current iteration element and 0-based index for ForEach-style loops~ .
    /// </summary>
    /// <param name="item">The current collection element.</param>
    /// <param name="index">The 0-based position of <paramref name="item"/> in the collection.</param>
    public void SetCurrentElement(object? item, int index)
    {
        Item = item;
        Index = index;
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"LoopContext(Id={LoopId}, Iter={Iteration}, Break={BreakRequested}, Continue={ContinueRequested})";
}
