// <copyright file="ScriptApprovalStore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Approvals;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 🛡️ §9.2 — the script approval catalog. The in-memory dictionary is authoritative (so the
/// compile-time <see cref="IsApproved"/> check is a cheap, synchronous lookup); every change is
/// written through to an optional <see cref="IScriptApprovalPersistence"/> as one JSON document,
/// mirroring the persisted script library store~ ✨.
/// </summary>
public sealed class ScriptApprovalStore : IScriptApprovalStore
{
    /// <summary>Unrequested candidates beyond this many are pruned, oldest first.</summary>
    public const int MaxCandidates = 500;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly IScriptApprovalPersistence? persistence;
    private readonly Func<DateTimeOffset> clock;
    private readonly object gate = new();
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private Dictionary<string, ScriptApprovalRecord>? records;

    /// <summary>Initializes a new instance of the <see cref="ScriptApprovalStore"/> class.</summary>
    /// <param name="persistence">Where to persist the catalog; null keeps it in memory only.</param>
    /// <param name="clock">Optional clock (tests).</param>
    public ScriptApprovalStore(IScriptApprovalPersistence? persistence = null, Func<DateTimeOffset>? clock = null)
    {
        this.persistence = persistence;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc/>
    public bool IsApproved(string codeHash)
    {
        lock (this.gate)
        {
            return this.Records().TryGetValue(codeHash, out var record)
                && record.Status == ScriptApprovalStatus.Approved;
        }
    }

    /// <inheritdoc/>
    public void RecordCandidate(string codeHash, string kind, string code, IReadOnlyList<SensitiveApiFlag> flags)
    {
        try
        {
            lock (this.gate)
            {
                var all = this.Records();
                if (all.ContainsKey(codeHash))
                {
                    return; // never downgrade a requested/approved/rejected record
                }

                all[codeHash] = new ScriptApprovalRecord(codeHash, kind, code, flags, ScriptApprovalStatus.Candidate, this.clock());
                PruneCandidates(all);
            }

            _ = this.PersistAsync(CancellationToken.None);
        }
        catch
        {
            // Recording is best-effort: the compile is denied either way.
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ScriptApprovalRecord>> ListAsync(ScriptApprovalStatus? status = null, CancellationToken ct = default)
    {
        lock (this.gate)
        {
            IReadOnlyList<ScriptApprovalRecord> list = this.Records().Values
                .Where(r => status is null || r.Status == status)
                .OrderByDescending(r => r.RequestedAt ?? r.FirstSeenAt)
                .ToList();
            return Task.FromResult(list);
        }
    }

    /// <inheritdoc/>
    public Task<ScriptApprovalRecord?> GetAsync(string codeHash, CancellationToken ct = default)
    {
        lock (this.gate)
        {
            return Task.FromResult(this.Records().TryGetValue(codeHash, out var r) ? r : null);
        }
    }

    /// <inheritdoc/>
    public async Task<ScriptApprovalResult> RequestAsync(string codeHash, string requestedBy, string? note, CancellationToken ct = default)
    {
        ScriptApprovalResult result;
        lock (this.gate)
        {
            var all = this.Records();
            if (!all.TryGetValue(codeHash, out var record))
            {
                return new ScriptApprovalResult(ScriptApprovalOutcome.NotFound, null);
            }

            if (record.Status is ScriptApprovalStatus.Approved or ScriptApprovalStatus.Requested)
            {
                return new ScriptApprovalResult(ScriptApprovalOutcome.InvalidState, record);
            }

            // Candidate, or Rejected being re-requested: clear any previous decision~
            record = record with
            {
                Status = ScriptApprovalStatus.Requested,
                RequestedBy = requestedBy,
                RequestedAt = this.clock(),
                RequestNote = note,
                DecidedBy = null,
                DecidedAt = null,
                DecisionNote = null,
            };
            all[codeHash] = record;
            result = new ScriptApprovalResult(ScriptApprovalOutcome.Ok, record);
        }

        await this.PersistAsync(ct).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc/>
    public async Task<ScriptApprovalResult> DecideAsync(string codeHash, string decidedBy, bool approve, string? note, CancellationToken ct = default)
    {
        ScriptApprovalResult result;
        lock (this.gate)
        {
            var all = this.Records();
            if (!all.TryGetValue(codeHash, out var record))
            {
                return new ScriptApprovalResult(ScriptApprovalOutcome.NotFound, null);
            }

            if (record.Status != ScriptApprovalStatus.Requested)
            {
                return new ScriptApprovalResult(ScriptApprovalOutcome.InvalidState, record);
            }

            if (string.Equals(record.RequestedBy, decidedBy, StringComparison.Ordinal))
            {
                return new ScriptApprovalResult(ScriptApprovalOutcome.SelfApproval, record);
            }

            record = record with
            {
                Status = approve ? ScriptApprovalStatus.Approved : ScriptApprovalStatus.Rejected,
                DecidedBy = decidedBy,
                DecidedAt = this.clock(),
                DecisionNote = note,
            };
            all[codeHash] = record;
            result = new ScriptApprovalResult(ScriptApprovalOutcome.Ok, record);
        }

        await this.PersistAsync(ct).ConfigureAwait(false);
        return result;
    }

    private static void PruneCandidates(Dictionary<string, ScriptApprovalRecord> all)
    {
        var candidates = all.Values.Where(r => r.Status == ScriptApprovalStatus.Candidate).ToList();
        if (candidates.Count <= MaxCandidates)
        {
            return;
        }

        foreach (var stale in candidates.OrderBy(r => r.FirstSeenAt).Take(candidates.Count - MaxCandidates))
        {
            all.Remove(stale.CodeHash);
        }
    }

    /// <summary>Loads the catalog on first use (caller holds <see cref="gate"/>).</summary>
    private Dictionary<string, ScriptApprovalRecord> Records()
    {
        if (this.records is not null)
        {
            return this.records;
        }

        var loaded = new Dictionary<string, ScriptApprovalRecord>(StringComparer.Ordinal);
        if (this.persistence is not null)
        {
            try
            {
                // One-time synchronous load: the compile-time gate is synchronous by design.
                var json = this.persistence.ReadAsync().GetAwaiter().GetResult();
                if (!string.IsNullOrWhiteSpace(json))
                {
                    foreach (var r in JsonSerializer.Deserialize<List<ScriptApprovalRecord>>(json, JsonOptions) ?? new())
                    {
                        loaded[r.CodeHash] = r;
                    }
                }
            }
            catch (JsonException)
            {
                // Unreadable catalog → start empty (fail closed: nothing approved).
            }
        }

        this.records = loaded;
        return loaded;
    }

    private async Task PersistAsync(CancellationToken ct)
    {
        if (this.persistence is null)
        {
            return;
        }

        await this.writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string json;
            lock (this.gate)
            {
                json = JsonSerializer.Serialize(this.Records().Values.ToList(), JsonOptions);
            }

            await this.persistence.WriteAsync(json, ct).ConfigureAwait(false);
        }
        finally
        {
            this.writeLock.Release();
        }
    }
}
