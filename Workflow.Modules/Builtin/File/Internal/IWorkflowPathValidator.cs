// <copyright file="IWorkflowPathValidator.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.File.Internal;

/// <summary>
/// 🎯 The intent behind a path access, used to decide which policy rules apply~ 🛡️.
/// </summary>
public enum PathAccessIntent
{
    /// <summary>The path will be read from~ 📖.</summary>
    Read,

    /// <summary>The path will be written to (triggers blocked-extension checks)~ ✍️.</summary>
    Write,
}

/// <summary>
/// ✅ The outcome of a path validation~ either a resolved absolute path or a reason for rejection.
/// </summary>
/// <param name="IsValid">Whether the path passed the sandbox.</param>
/// <param name="ResolvedPath">The canonical absolute path when valid; otherwise <c>null</c>.</param>
/// <param name="Reason">The rejection reason when invalid; otherwise <c>null</c>.</param>
public record PathValidationResult(bool IsValid, string? ResolvedPath, string? Reason)
{
    /// <summary>
    /// Creates a successful validation result~ ✨.
    /// </summary>
    /// <param name="resolvedPath">The canonical absolute path.</param>
    /// <returns>A valid <see cref="PathValidationResult"/>.</returns>
    public static PathValidationResult Ok(string resolvedPath)
        => new(true, resolvedPath, null);

    /// <summary>
    /// Creates a failed validation result~ 🚫.
    /// </summary>
    /// <param name="reason">The rejection reason.</param>
    /// <returns>An invalid <see cref="PathValidationResult"/>.</returns>
    public static PathValidationResult Reject(string reason)
        => new(false, null, reason);
}

/// <summary>
/// 🛡️ The single gate every file-touching module must pass user paths through~ 📁✨.
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.5.a.0. Modules never call <c>File.ReadAllText(userInput)</c> directly —
/// they resolve <see cref="IWorkflowPathValidator"/> from <c>context.Services</c> and validate first.
/// The implementation canonicalises the path, rejects traversal / root escapes / (write) blocked
/// extensions, and optionally re-checks symlink targets~ 🚫.
/// </remarks>
public interface IWorkflowPathValidator
{
    /// <summary>
    /// Validates and canonicalises a raw user path~ 🛡️.
    /// </summary>
    /// <param name="rawPath">The raw path from module properties.</param>
    /// <param name="intent">Whether the path will be read or written.</param>
    /// <returns>A <see cref="PathValidationResult"/> with the resolved path or a reason.</returns>
    PathValidationResult ValidatePath(string rawPath, PathAccessIntent intent);
}
