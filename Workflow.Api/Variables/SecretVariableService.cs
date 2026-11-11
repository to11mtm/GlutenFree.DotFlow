// <copyright file="SecretVariableService.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Variables;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;

/// <summary>🔒 Plan 08 — one stored variable's secret settings and who/when trail.</summary>
public sealed class SecretVariableState
{
    /// <summary>Gets or sets the mode.</summary>
    public SecretVariableMode Mode { get; set; }

    /// <summary>Gets or sets a value indicating whether a follow-mode variable has become secret (it then stays secret).</summary>
    public bool Sticky { get; set; }

    /// <summary>Gets or sets who changed it and when (no values).</summary>
    public List<SecretVariableAuditEntry> Audit { get; set; } = new();
}

/// <summary>🔒 Plan 08 — one change to a secret variable (never its value).</summary>
/// <param name="At">When.</param>
/// <param name="By">Who (a user, or "a workflow run").</param>
/// <param name="Action">What happened (e.g. "set", "marked secret", "earlier values erased").</param>
public sealed record SecretVariableAuditEntry(DateTimeOffset At, string By, string Action);

/// <summary>
/// 🔒 Plan 08 — which stored variables are secret: each one's mode, whether a follow-mode variable
/// has become secret, and a who/when trail. Kept as one document in the blob store (no change to the
/// variable stores' schemas), reloaded every minute so every server sees changes.
/// </summary>
public sealed class SecretVariableService : ISecretVariableCatalog
{
    /// <summary>The most audit entries kept per variable.</summary>
    public const int MaxAuditEntries = 50;

    private const string BlobKey = "settings/secret-variables.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly TimeSpan DeclarationCacheTtl = TimeSpan.FromMinutes(1);

    private readonly IBlobStore? blobs;
    private readonly Func<IWorkflowRepository?> workflows;
    private readonly Func<DateTimeOffset> clock;
    private readonly SemaphoreSlim gate = new(1, 1);
    private volatile Dictionary<string, SecretVariableState> states = new(StringComparer.Ordinal);
    private (DateTimeOffset At, HashSet<string> Names)? declaredAnywhere;

    /// <summary>Initializes a new instance of the <see cref="SecretVariableService"/> class.</summary>
    /// <param name="blobs">Where the document lives (null = memory only).</param>
    /// <param name="workflows">The workflow repository (for "follow workflow declarations").</param>
    /// <param name="clock">Clock (tests).</param>
    public SecretVariableService(IBlobStore? blobs, Func<IWorkflowRepository?> workflows, Func<DateTimeOffset>? clock = null)
    {
        this.blobs = blobs;
        this.workflows = workflows;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The key a variable's state is filed under.</summary>
    /// <param name="scope">The scope.</param>
    /// <param name="name">The variable name.</param>
    /// <returns>The key.</returns>
    public static string Key(VariableScope scope, string name)
        => $"{scope.Kind}:{scope.WorkflowId?.ToString() ?? scope.ExecutionId?.ToString() ?? "-"}:{name}";

    /// <summary>Loads (or reloads) the document.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (this.blobs is null)
        {
            return;
        }

        await using var stream = await this.blobs.GetAsync(BlobKey, ct).ConfigureAwait(false);
        if (stream is null)
        {
            return;
        }

        var loaded = await JsonSerializer.DeserializeAsync<Dictionary<string, SecretVariableState>>(stream, Json, ct).ConfigureAwait(false);
        this.states = new Dictionary<string, SecretVariableState>(loaded ?? new(), StringComparer.Ordinal);
    }

    /// <summary>A variable's state, or null when it has never been marked.</summary>
    /// <param name="scope">The scope.</param>
    /// <param name="name">The name.</param>
    /// <returns>The state, or null.</returns>
    public SecretVariableState? Get(VariableScope scope, string name)
        => this.states.TryGetValue(Key(scope, name), out var state) ? state : null;

    /// <summary>
    /// Whether a variable is secret right now. A follow-mode variable that a workflow now declares
    /// secret becomes (and stays) secret; <paramref name="becameSecret"/> says so, so the caller can
    /// encrypt its value and erase earlier ones.
    /// </summary>
    /// <param name="scope">The scope.</param>
    /// <param name="name">The name.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Whether it's secret, and whether it has just become secret.</returns>
    public async Task<(bool IsSecret, bool BecameSecret)> ResolveAsync(VariableScope scope, string name, CancellationToken ct = default)
    {
        var state = this.Get(scope, name);
        switch (state?.Mode)
        {
            case SecretVariableMode.Secret:
                return (true, false);
            case SecretVariableMode.FollowWorkflows when state.Sticky:
                return (true, false);
            case SecretVariableMode.FollowWorkflows:
                if (await this.DeclaredSecretAsync(scope, name, ct).ConfigureAwait(false))
                {
                    await this.UpdateAsync(scope, name, s => s.Sticky = true, "a workflow declared it secret", "became secret", ct).ConfigureAwait(false);
                    return (true, true);
                }

                return (false, false);
            default:
                return (false, false);
        }
    }

    /// <summary>Whether a workflow declares this name secret (workflow scope: that workflow; global: any).</summary>
    /// <param name="scope">The scope.</param>
    /// <param name="name">The name.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>True when declared secret.</returns>
    public async Task<bool> DeclaredSecretAsync(VariableScope scope, string name, CancellationToken ct = default)
    {
        if (this.workflows() is not { } repository)
        {
            return false;
        }

        if (scope.Kind == VariableScopeKind.Workflow && scope.WorkflowId is { } workflowId)
        {
            var definition = await repository.GetByIdAsync(workflowId, ct).ConfigureAwait(false);
            return definition is not null && DeclaresSecret(definition, name);
        }

        if (this.declaredAnywhere is not { } cached || this.clock() - cached.At > DeclarationCacheTtl)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var page = 1; ; page++)
            {
                var batch = await repository.GetAllAsync(new WorkflowFilter(), new Pagination(page, 200), ct).ConfigureAwait(false);
                foreach (var definition in batch.Items)
                {
                    names.UnionWith(definition.Variables.Values.Where(v => v.IsSecret).Select(v => v.Name));
                }

                if (batch.Items.Count < 200)
                {
                    break;
                }
            }

            this.declaredAnywhere = cached = (this.clock(), names);
        }

        return cached.Names.Contains(name);
    }

    /// <summary>Forgets the cached workflow declarations (a workflow was saved).</summary>
    public void WorkflowsChanged() => this.declaredAnywhere = null;

    /// <summary>Sets a variable's mode (and, for Not secret, clears its "became secret").</summary>
    /// <param name="scope">The scope.</param>
    /// <param name="name">The name.</param>
    /// <param name="mode">The mode.</param>
    /// <param name="by">Who.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task.</returns>
    public Task SetModeAsync(VariableScope scope, string name, SecretVariableMode mode, string by, CancellationToken ct = default)
        => this.UpdateAsync(
            scope,
            name,
            s =>
            {
                // A secret switched to "follow" stays secret (it already is); Not secret clears it~
                s.Sticky = mode switch
                {
                    SecretVariableMode.None => false,
                    SecretVariableMode.FollowWorkflows => s.Sticky || s.Mode == SecretVariableMode.Secret,
                    _ => s.Sticky,
                };
                s.Mode = mode;
            },
            by,
            mode switch
            {
                SecretVariableMode.Secret => "marked secret",
                SecretVariableMode.FollowWorkflows => "set to follow workflow declarations",
                _ => "marked not secret",
            },
            ct);

    /// <summary>Records who did what to a variable (never the value).</summary>
    /// <param name="scope">The scope.</param>
    /// <param name="name">The name.</param>
    /// <param name="by">Who.</param>
    /// <param name="action">What.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task.</returns>
    public Task AuditAsync(VariableScope scope, string name, string by, string action, CancellationToken ct = default)
        => this.UpdateAsync(scope, name, _ => { }, by, action, ct);

    /// <summary>Forgets a deleted variable.</summary>
    /// <param name="scope">The scope.</param>
    /// <param name="name">The name.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task ForgetAsync(VariableScope scope, string name, CancellationToken ct = default)
    {
        await this.gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var next = new Dictionary<string, SecretVariableState>(this.states, StringComparer.Ordinal);
            if (next.Remove(Key(scope, name)))
            {
                await this.SaveAsync(next, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            this.gate.Release();
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyCollection<string>> SecretNamesAsync(VariableScope scope, CancellationToken ct = default)
    {
        var prefix = Key(scope, string.Empty);
        IReadOnlyCollection<string> names = this.states
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal)
                         && (kv.Value.Mode == SecretVariableMode.Secret || (kv.Value.Mode == SecretVariableMode.FollowWorkflows && kv.Value.Sticky)))
            .Select(kv => kv.Key[prefix.Length..])
            .ToList();
        return Task.FromResult(names);
    }

    /// <summary>The stored follow-mode variables that saving this workflow would turn secret (for the designer's confirmation).</summary>
    /// <param name="definition">The workflow about to be saved.</param>
    /// <returns>Scope + name of each affected variable.</returns>
    public IReadOnlyList<(VariableScope Scope, string Name)> FollowersTurnedSecretBy(WorkflowDefinition definition)
    {
        var declared = definition.Variables.Values.Where(v => v.IsSecret).Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var affected = new List<(VariableScope, string)>();
        foreach (var name in declared)
        {
            foreach (var scope in new[] { VariableScope.Global, VariableScope.ForWorkflow(definition.Id) })
            {
                if (this.Get(scope, name) is { Mode: SecretVariableMode.FollowWorkflows, Sticky: false })
                {
                    affected.Add((scope, name));
                }
            }
        }

        return affected;
    }

    private static bool DeclaresSecret(WorkflowDefinition definition, string name)
        => definition.Variables.Values.Any(v => v.IsSecret && string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

    private async Task UpdateAsync(VariableScope scope, string name, Action<SecretVariableState> change, string by, string action, CancellationToken ct)
    {
        await this.gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var next = new Dictionary<string, SecretVariableState>(this.states, StringComparer.Ordinal);
            var key = Key(scope, name);
            var state = next.TryGetValue(key, out var existing)
                ? new SecretVariableState { Mode = existing.Mode, Sticky = existing.Sticky, Audit = new List<SecretVariableAuditEntry>(existing.Audit) }
                : new SecretVariableState();
            change(state);
            state.Audit = state.Audit.Append(new SecretVariableAuditEntry(this.clock(), by, action)).TakeLast(MaxAuditEntries).ToList();
            next[key] = state;
            await this.SaveAsync(next, ct).ConfigureAwait(false);
        }
        finally
        {
            this.gate.Release();
        }
    }

    private async Task SaveAsync(Dictionary<string, SecretVariableState> next, CancellationToken ct)
    {
        if (this.blobs is not null)
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(next, Json)));
            await this.blobs.PutAsync(BlobKey, stream, "application/json", ct).ConfigureAwait(false);
        }

        this.states = next;
    }
}

/// <summary>🔒 Plan 08 — loads the secret-variable settings at startup and every minute (other servers' changes).</summary>
public sealed class SecretVariableHostedService : BackgroundService
{
    private readonly SecretVariableService service;
    private readonly ILogger<SecretVariableHostedService> logger;

    /// <summary>Initializes a new instance of the <see cref="SecretVariableHostedService"/> class.</summary>
    /// <param name="service">The service.</param>
    /// <param name="logger">The logger.</param>
    public SecretVariableHostedService(SecretVariableService service, ILogger<SecretVariableHostedService> logger)
    {
        this.service = service;
        this.logger = logger;
    }

    /// <inheritdoc/>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await this.service.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this.logger.LogError(ex, "🔒 Couldn't load the secret-variable settings~");
        }

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await this.service.LoadAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    this.logger.LogWarning(ex, "🔒 Couldn't reload the secret-variable settings; keeping the last ones~");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down~
        }
    }
}
