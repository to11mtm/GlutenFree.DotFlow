// <copyright file="VariableEntry.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Models;

/// <summary>
/// 💾 A versioned entry in the variable store.
/// Represents a single version of a variable's value~ ✨
/// </summary>
/// <remarks>
/// CopilotNote: A <c>VariableEntry</c> with <c>Value = null</c> is a valid entry (not deleted!).
/// Use <c>IVariableStore.DeleteVariableAsync</c> to actually remove a variable. If
/// <c>GetVariableAsync</c> returns <c>null</c> (not a VariableEntry), the variable does not exist~ 💖
/// </remarks>
public record VariableEntry(
    VariableScope Scope,
    string Name,
    object? Value,
    string ValueTypeName,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

