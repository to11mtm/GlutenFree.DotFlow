// <copyright file="IVariableStore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Abstractions;

using Workflow.Persistence.Models;

/// <summary>
/// 💾 Versioned variable store with scoped isolation and history tracking~ ✨💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: <b>Null value semantics are critical here!</b>
/// <list type="bullet">
///   <item><c>SetVariableAsync(scope, "x", null)</c> → creates a new versioned entry with <c>Value = null</c>. This is a valid entry.</item>
///   <item><c>GetVariableAsync</c> returns <c>null</c> → variable does not exist at all.</item>
///   <item><c>GetVariableAsync</c> returns <c>VariableEntry { Value = null }</c> → variable exists but its value is explicitly null.</item>
///   <item><c>DeleteVariableAsync</c> → hard removes the variable and all its history. The only way to truly delete.</item>
/// </list>
/// </para>
/// </remarks>
public interface IVariableStore
{
    /// <summary>
    /// Sets a variable value, creating a new version. A <c>null</c> value is persisted as a valid
    /// null-valued entry (not a delete)~ 💾.
    /// </summary>
    Task SetVariableAsync(VariableScope scope, string name, object? value, CancellationToken ct = default);

    /// <summary>
    /// Gets a variable entry. Returns <c>null</c> if the variable does not exist.
    /// Returns <c>VariableEntry { Value = null }</c> if the variable exists with a null value~ 🔍.
    /// </summary>
    /// <param name="scope">The variable scope.</param>
    /// <param name="name">The variable name.</param>
    /// <param name="version">Optional specific version. <c>null</c> returns the latest version.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<VariableEntry?> GetVariableAsync(VariableScope scope, string name, int? version = null, CancellationToken ct = default);

    /// <summary>Gets the full version history of a variable, ordered by version ascending~ 📜.</summary>
    Task<IReadOnlyList<VariableEntry>> GetVariableHistoryAsync(VariableScope scope, string name, CancellationToken ct = default);

    /// <summary>
    /// Hard-deletes a variable and all its version history from the given scope.
    /// Returns <c>false</c> if the variable did not exist~ 🗑️.
    /// </summary>
    Task<bool> DeleteVariableAsync(VariableScope scope, string name, CancellationToken ct = default);

    /// <summary>
    /// Gets the latest version of all variables in the given scope.
    /// Includes variables whose current value is <c>null</c>~ 📋.
    /// </summary>
    Task<IReadOnlyDictionary<string, object?>> GetAllVariablesAsync(VariableScope scope, CancellationToken ct = default);
}

