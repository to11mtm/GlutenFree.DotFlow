// <copyright file="RunRequest.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// ▶️ Phase 3.5 (V5) — what the run dialog produces: the values to start an execution with, and
/// how any variable writes during that run should be persisted~ ✨.
/// </summary>
/// <param name="Inputs">The starting values, keyed by variable name.</param>
/// <param name="VariableWriteMode">
/// <c>execution</c> (default), <c>workflow</c>, or <c>dual</c>. The API has always accepted this;
/// the designer simply never sent it, so "does this run's writes persist?" was unanswerable from
/// the UI.
/// </param>
public sealed record RunRequest(
    Dictionary<string, JsonElement> Inputs,
    string? VariableWriteMode);
