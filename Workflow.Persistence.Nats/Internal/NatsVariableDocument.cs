// <copyright file="NatsVariableDocument.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Nats.Internal;

/// <summary>
/// 💾 Internal document stored per variable version in the <c>WF_VARIABLES</c> KV bucket~ ✨
/// </summary>
/// <remarks>
/// CopilotNote: Each <c>SetVariableAsync</c> call appends a new NATS KV revision.
/// The <c>Version</c> field is a sequential counter (1-based) that we track explicitly —
/// NATS KV global revision numbers are not per-key, so we cannot rely on them for user-facing version numbers~ 🧠
/// </remarks>
internal record NatsVariableDocument(
    object? Value,
    string ValueTypeName,
    int Version,
    string CreatedAt,
    string UpdatedAt);

