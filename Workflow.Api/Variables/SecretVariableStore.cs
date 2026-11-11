// <copyright file="SecretVariableStore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Variables;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;

/// <summary>
/// 🔒 Plan 08 — wraps the persistence provider's variable store so secret variables (global /
/// workflow scope) are stored <b>encrypted</b> with Data Protection and keep <b>no earlier values</b>:
/// each write of a secret erases its older versions. Runs read the real value; the API never shows
/// it (see <c>VariableEndpoints</c>).
/// </summary>
/// <remarks>
/// A workflow-scope write of a variable the workflow declares <c>IsSecret</c> ("Save for future
/// runs") marks it Secret automatically. Execution-scope records are already redacted by the engine.
/// </remarks>
public sealed class SecretVariableStore : IVariableStore
{
    /// <summary>The prefix of an encrypted value.</summary>
    public const string Prefix = "dp:v1:";

    /// <summary>Who changes a variable when nobody signed in is named (a run, a background job).</summary>
    public const string RunCaller = "a workflow run";

    private static readonly AsyncLocal<string?> CurrentCaller = new();

    private readonly IVariableStore inner;
    private readonly SecretVariableService secrets;
    private readonly IDataProtector protector;

    /// <summary>Initializes a new instance of the <see cref="SecretVariableStore"/> class.</summary>
    /// <param name="inner">The provider's store.</param>
    /// <param name="secrets">Which variables are secret.</param>
    /// <param name="protection">Data Protection.</param>
    public SecretVariableStore(IVariableStore inner, SecretVariableService secrets, IDataProtectionProvider protection)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        this.protector = protection.CreateProtector("DotFlow.SecretVariables.v1");
    }

    /// <summary>Gets or sets who is changing variables in this async flow (set by the API for the signed-in user).</summary>
    public static string? Caller
    {
        get => CurrentCaller.Value;
        set => CurrentCaller.Value = value;
    }

    /// <summary>Gets the wrapped store (tests check what is really stored).</summary>
    public IVariableStore Inner => this.inner;

    /// <inheritdoc/>
    public async Task SetVariableAsync(VariableScope scope, string name, object? value, CancellationToken ct = default)
    {
        if (scope.Kind == VariableScopeKind.Execution)
        {
            await this.inner.SetVariableAsync(scope, name, value, ct).ConfigureAwait(false);
            return;
        }

        // "Save for future runs" of a variable its workflow declares secret → Secret, automatically~
        if (scope.Kind == VariableScopeKind.Workflow
            && this.secrets.Get(scope, name) is null
            && await this.secrets.DeclaredSecretAsync(scope, name, ct).ConfigureAwait(false))
        {
            await this.secrets.SetModeAsync(scope, name, SecretVariableMode.Secret, Caller ?? RunCaller, ct).ConfigureAwait(false);
        }

        var (isSecret, _) = await this.secrets.ResolveAsync(scope, name, ct).ConfigureAwait(false);
        if (!isSecret)
        {
            await this.inner.SetVariableAsync(scope, name, value, ct).ConfigureAwait(false);
            return;
        }

        await this.WriteSecretAsync(scope, name, value, ct).ConfigureAwait(false);
        await this.secrets.AuditAsync(scope, name, Caller ?? RunCaller, "set", ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<VariableEntry?> GetVariableAsync(VariableScope scope, string name, int? version = null, CancellationToken ct = default)
        => this.Open(await this.inner.GetVariableAsync(scope, name, version, ct).ConfigureAwait(false));

    /// <inheritdoc/>
    public async Task<IReadOnlyList<VariableEntry>> GetVariableHistoryAsync(VariableScope scope, string name, CancellationToken ct = default)
        => (await this.inner.GetVariableHistoryAsync(scope, name, ct).ConfigureAwait(false)).Select(e => this.Open(e)!).ToList();

    /// <inheritdoc/>
    public async Task<bool> DeleteVariableAsync(VariableScope scope, string name, CancellationToken ct = default)
    {
        var deleted = await this.inner.DeleteVariableAsync(scope, name, ct).ConfigureAwait(false);
        if (deleted)
        {
            await this.secrets.ForgetAsync(scope, name, ct).ConfigureAwait(false);
        }

        return deleted;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, object?>> GetAllVariablesAsync(VariableScope scope, CancellationToken ct = default)
    {
        var all = await this.inner.GetAllVariablesAsync(scope, ct).ConfigureAwait(false);
        return all.ToDictionary(kv => kv.Key, kv => this.Decrypt(kv.Value));
    }

    /// <summary>How many stored versions a variable has (what marking it secret would erase).</summary>
    /// <param name="scope">The scope.</param>
    /// <param name="name">The name.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The version count.</returns>
    public async Task<int> VersionCountAsync(VariableScope scope, string name, CancellationToken ct = default)
        => (await this.inner.GetVariableHistoryAsync(scope, name, ct).ConfigureAwait(false)).Count;

    /// <summary>
    /// Turns a stored variable's value secret: re-stores the current value encrypted and erases every
    /// earlier version.
    /// </summary>
    /// <param name="scope">The scope.</param>
    /// <param name="name">The name.</param>
    /// <param name="by">Who.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>How many earlier versions were erased.</returns>
    public async Task<int> ConvertToSecretAsync(VariableScope scope, string name, string by, CancellationToken ct = default)
    {
        var history = await this.inner.GetVariableHistoryAsync(scope, name, ct).ConfigureAwait(false);
        if (history.Count == 0)
        {
            return 0;
        }

        var current = this.Open(history.OrderByDescending(e => e.Version).First())!;
        await this.WriteSecretAsync(scope, name, current.Value, ct).ConfigureAwait(false);
        var erased = history.Count;
        await this.secrets.AuditAsync(scope, name, by, $"earlier values erased ({erased} version{(erased == 1 ? string.Empty : "s")})", ct).ConfigureAwait(false);
        return erased;
    }

    /// <summary>Stores a plain value again (switching a secret to Not secret needs the value re-entered).</summary>
    /// <param name="scope">The scope.</param>
    /// <param name="name">The name.</param>
    /// <param name="value">The new value.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task ReplaceWithPlainAsync(VariableScope scope, string name, object? value, CancellationToken ct = default)
    {
        await this.inner.DeleteVariableAsync(scope, name, ct).ConfigureAwait(false);
        await this.inner.SetVariableAsync(scope, name, value, ct).ConfigureAwait(false);
    }

    private async Task WriteSecretAsync(VariableScope scope, string name, object? value, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(value);
        var encrypted = Prefix + this.protector.Protect(json);

        // A secret keeps no earlier values, encrypted or not: replace, don't append a version~
        await this.inner.DeleteVariableAsync(scope, name, ct).ConfigureAwait(false);
        await this.inner.SetVariableAsync(scope, name, encrypted, ct).ConfigureAwait(false);
    }

    private VariableEntry? Open(VariableEntry? entry)
        => entry is null ? null : entry with { Value = this.Decrypt(entry.Value) };

    private object? Decrypt(object? value)
    {
        var text = value switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
            _ => null,
        };
        if (text is null || !text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return value;
        }

        var json = this.protector.Unprotect(text[Prefix.Length..]);
        using var document = JsonDocument.Parse(json);
        return Workflow.Modules.Internal.JsonValueConverter.FromElement(document.RootElement.Clone());
    }
}
