// <copyright file="ScriptSecurityException.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Api;

using System;

/// <summary>
/// 🔒 Phase 3.1 — Thrown when a script attempts a capability denied by its
/// <see cref="Workflow.Scripting.Abstractions.ScriptExecutionConfig"/> (e.g. network/file access when
/// disallowed, or a path outside the allowed set)~ ✨.
/// </summary>
public sealed class ScriptSecurityException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ScriptSecurityException"/> class~ 🔒.</summary>
    /// <param name="message">The reason the capability was denied.</param>
    public ScriptSecurityException(string message)
        : base(message)
    {
    }
}
