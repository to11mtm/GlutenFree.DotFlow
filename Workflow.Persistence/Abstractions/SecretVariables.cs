// <copyright file="SecretVariables.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Abstractions;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Workflow.Persistence.Models;

/// <summary>🔒 Plan 08 — whether a stored (global / workflow-scope) variable is secret.</summary>
public enum SecretVariableMode
{
    /// <summary>Not secret: stored and shown as is.</summary>
    None,

    /// <summary>Secret: stored encrypted, write-only, no earlier values kept.</summary>
    Secret,

    /// <summary>
    /// Secret while a workflow declares a variable of this name <c>IsSecret</c> (for workflow scope,
    /// that workflow). Once it has become secret it stays secret.
    /// </summary>
    FollowWorkflows,
}

/// <summary>
/// 🔒 Plan 08 — which stored variables are secret, so a run can hide their values in history,
/// logs and events (on top of the variables its workflow declares secret).
/// </summary>
public interface ISecretVariableCatalog
{
    /// <summary>The names of the secret variables stored in a scope.</summary>
    /// <param name="scope">The scope.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The names.</returns>
    Task<IReadOnlyCollection<string>> SecretNamesAsync(VariableScope scope, CancellationToken ct = default);
}
