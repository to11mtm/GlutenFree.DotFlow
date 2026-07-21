// <copyright file="IModulePackageArchive.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Packaging;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 🗄️ Phase 2.8.0 — Optional seam for archiving the raw <c>.wfmod</c> bytes for re-provisioning
/// (Q1). The host implements this over its blob store; when unregistered, archival is skipped~ ✨.
/// </summary>
/// <remarks>
/// Kept in <c>Workflow.Modules</c> (rather than depending on <c>Workflow.Persistence</c>) so the
/// module layer stays free of a persistence reference — the API host adapts <c>IBlobStore</c> to
/// this seam~ 🌸.
/// </remarks>
public interface IModulePackageArchive
{
    /// <summary>Archives a package's raw bytes keyed by module id + version~ 🗄️.</summary>
    /// <param name="moduleId">The module id.</param>
    /// <param name="version">The module version.</param>
    /// <param name="packageBytes">The raw <c>.wfmod</c> bytes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the bytes are archived.</returns>
    Task ArchiveAsync(string moduleId, Version version, byte[] packageBytes, CancellationToken ct = default);
}
