// <copyright file="FileModuleStateStore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.State;

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 🗂️ Phase 2.8.2 — The **default** module state store: a JSON file (<c>state.json</c>) under the
/// packages root~ ✨.
/// </summary>
public sealed class FileModuleStateStore : IModuleStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string filePath;
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>Initializes a new instance of the <see cref="FileModuleStateStore"/> class~ 🗂️.</summary>
    /// <param name="filePath">The absolute path to the state JSON file.</param>
    public FileModuleStateStore(string filePath)
    {
        this.filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    /// <inheritdoc/>
    public async Task<ModuleStateSnapshot> LoadAsync(CancellationToken ct = default)
    {
        await this.gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(this.filePath))
            {
                return ModuleStateSnapshot.Empty;
            }

            var json = await File.ReadAllTextAsync(this.filePath, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<ModuleStateSnapshot>(json, JsonOptions) ?? ModuleStateSnapshot.Empty;
        }
        catch (JsonException)
        {
            return ModuleStateSnapshot.Empty;
        }
        finally
        {
            this.gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task SaveAsync(ModuleStateSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await this.gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var dir = Path.GetDirectoryName(this.filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            await File.WriteAllTextAsync(this.filePath, json, ct).ConfigureAwait(false);
        }
        finally
        {
            this.gate.Release();
        }
    }
}
