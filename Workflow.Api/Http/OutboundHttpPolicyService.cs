// <copyright file="OutboundHttpPolicyService.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Http;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Workflow.Core.Http;
using Workflow.Persistence.Abstractions;

/// <summary>
/// 🌐 Stage 7.3 — the outbound HTTP policy in force: host settings (<c>Http:Outbound</c>, which are
/// <b>locked</b>) merged over the policy saved from the setup screen, falling back to the
/// fail-closed <see cref="EffectiveOutboundPolicy.UnconfiguredMode"/>. Saves are validated and
/// audited. See <c>docs/outbound-http.md</c> and plan 06.
/// </summary>
public sealed class OutboundHttpPolicyService : IOutboundHttpPolicyProvider
{
    /// <summary>The most audit entries kept.</summary>
    public const int MaxAuditEntries = 200;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly OutboundHttpOptions host;
    private readonly IOutboundHttpPolicyPersistence persistence;
    private readonly Func<DateTimeOffset> clock;
    private readonly SemaphoreSlim gate = new(1, 1);
    private volatile StoredDocument document = new();

    /// <summary>Initializes a new instance of the <see cref="OutboundHttpPolicyService"/> class.</summary>
    /// <param name="host">The host settings (locked).</param>
    /// <param name="persistence">Where the saved policy and audit live.</param>
    /// <param name="clock">Clock (tests).</param>
    public OutboundHttpPolicyService(OutboundHttpOptions? host, IOutboundHttpPolicyPersistence persistence, Func<DateTimeOffset>? clock = null)
    {
        this.host = host ?? new OutboundHttpOptions();
        this.persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Gets the channels, in display order.</summary>
    public static IReadOnlyList<OutboundHttpChannel> Channels { get; } = [OutboundHttpChannel.HttpModule, OutboundHttpChannel.Scripts];

    /// <inheritdoc/>
    public EffectiveOutboundPolicy Get(OutboundHttpChannel channel) => EffectiveOutboundPolicy.Resolve(channel, this.host, this.document.Saved);

    /// <summary>Loads (or reloads) the saved policy.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var json = await this.persistence.ReadAsync(ct).ConfigureAwait(false);
        this.document = string.IsNullOrWhiteSpace(json)
            ? new StoredDocument()
            : JsonSerializer.Deserialize<StoredDocument>(json, Json) ?? new StoredDocument();
    }

    /// <summary>What the setup screen shows: per channel, the effective policy, the saved values and which fields are locked.</summary>
    /// <returns>The view.</returns>
    public OutboundHttpSettingsView View()
    {
        var saved = this.document.Saved ?? new OutboundHttpOptions();
        return new OutboundHttpSettingsView(
            Channels.Select(c => new OutboundHttpChannelView(c, this.Get(c), saved.For(c), this.host.For(c))).ToList(),
            saved.Default,
            this.host.Default,
            EffectiveOutboundPolicy.UnconfiguredMode);
    }

    /// <summary>Gets the audit trail, newest first.</summary>
    /// <returns>The entries.</returns>
    public IReadOnlyList<OutboundHttpAuditEntry> Audit() => this.document.Audit.AsEnumerable().Reverse().ToList();

    /// <summary>Saves the policy from the setup screen (replacing the saved layer).</summary>
    /// <param name="proposed">The new saved policy.</param>
    /// <param name="caller">Who saved it (audit).</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Validation errors (empty = saved).</returns>
    public async Task<IReadOnlyList<string>> SaveAsync(OutboundHttpOptions proposed, string caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        var errors = this.Validate(proposed);
        if (errors.Count > 0)
        {
            return errors;
        }

        await this.gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var before = this.document.Saved ?? new OutboundHttpOptions();
            var changes = Describe(before, proposed);
            var audit = this.document.Audit
                .Append(new OutboundHttpAuditEntry(this.clock(), caller, changes.Count == 0 ? ["no changes"] : changes))
                .TakeLast(MaxAuditEntries)
                .ToList();
            var next = new StoredDocument { Saved = proposed, Audit = audit };
            await this.persistence.WriteAsync(JsonSerializer.Serialize(next, Json), ct).ConfigureAwait(false);
            this.document = next;
            return [];
        }
        finally
        {
            this.gate.Release();
        }
    }

    /// <summary>"Would this URL be allowed?" — host rules, then every resolved address.</summary>
    /// <param name="channel">The channel.</param>
    /// <param name="url">The URL.</param>
    /// <param name="nodeAllowedHosts">Per-node narrowing to simulate (optional).</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The outcome.</returns>
    public async Task<OutboundHttpCheckResult> CheckAsync(OutboundHttpChannel channel, string url, IReadOnlyCollection<string>? nodeAllowedHosts, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return new OutboundHttpCheckResult(false, $"'{url}' isn't an absolute URL", []);
        }

        var policy = this.Get(channel);
        var host = OutboundHttpRules.CheckHost(policy, uri, nodeAllowedHosts);
        if (!host.Allowed)
        {
            return new OutboundHttpCheckResult(false, host.Reason, []);
        }

        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(uri.Host.Trim('[', ']'), out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(uri.IdnHost, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ArgumentException)
        {
            return new OutboundHttpCheckResult(false, $"'{uri.Host}' doesn't resolve: {ex.Message}", []);
        }

        var results = addresses
            .Select(a => (Address: a, Decision: OutboundHttpRules.CheckAddress(policy, uri.IdnHost, a)))
            .Select(x => new OutboundHttpAddressCheck(x.Address.ToString(), x.Decision.Allowed, x.Decision.Reason))
            .ToList();
        var allowed = results.FirstOrDefault(r => r.Allowed);
        return allowed is not null
            ? new OutboundHttpCheckResult(true, allowed.Reason, results)
            : new OutboundHttpCheckResult(false, results.FirstOrDefault()?.Reason ?? "no addresses", results);
    }

    private List<string> Validate(OutboundHttpOptions proposed)
    {
        var errors = new List<string>();
        void Layer(string name, OutboundHttpPolicySettings? layer, IReadOnlyCollection<string> locked)
        {
            if (layer is null)
            {
                return;
            }

            foreach (var field in layer.SetFields().Where(locked.Contains))
            {
                errors.Add($"{name}.{field} is set by the server's configuration ({OutboundHttpOptions.SectionName}) and can't be changed here.");
            }

            foreach (var cidr in (layer.AllowedCidrs ?? []).Concat(layer.DeniedCidrs ?? []).Where(c => !OutboundHttpRules.IsValidCidr(c)))
            {
                errors.Add($"{name}: '{cidr}' isn't an address or address range (e.g. 10.20.0.0/16).");
            }

            foreach (var pattern in (layer.AllowedHosts ?? []).Concat(layer.DeniedHosts ?? []).Where(h => !IsHostPattern(h)))
            {
                errors.Add($"{name}: '{pattern}' isn't a host name (e.g. api.partner.com or *.partner.com) — no scheme, port or path.");
            }
        }

        Layer("Default", proposed.Default, this.host.Default?.SetFields().ToHashSet() ?? []);
        foreach (var channel in Channels)
        {
            Layer(channel.ToString(), proposed.For(channel), this.Get(channel).LockedFields);
        }

        return errors;
    }

    private static bool IsHostPattern(string value)
    {
        var v = (value ?? string.Empty).Trim();
        var body = v.StartsWith("*.", StringComparison.Ordinal) ? v[2..] : v;
        return body.Length > 0
               && Uri.CheckHostName(body) != UriHostNameType.Unknown
               && !v.Contains("://", StringComparison.Ordinal);
    }

    private static List<string> Describe(OutboundHttpOptions before, OutboundHttpOptions after)
    {
        var changes = new List<string>();
        void Compare(string name, OutboundHttpPolicySettings? b, OutboundHttpPolicySettings? a)
        {
            foreach (var field in OutboundHttpPolicySettings.FieldNames)
            {
                var was = Value(b, field);
                var now = Value(a, field);
                if (was != now)
                {
                    changes.Add($"{name}.{field}: {was ?? "(unset)"} → {now ?? "(unset)"}");
                }
            }
        }

        Compare("Default", before.Default, after.Default);
        Compare("Scripts", before.Scripts, after.Scripts);
        Compare("HttpModule", before.HttpModule, after.HttpModule);
        return changes;
    }

    private static string? Value(OutboundHttpPolicySettings? s, string field) => s is null ? null : field switch
    {
        nameof(OutboundHttpPolicySettings.Mode) => s.Mode?.ToString(),
        nameof(OutboundHttpPolicySettings.AllowedHosts) => s.AllowedHosts is null ? null : string.Join(", ", s.AllowedHosts),
        nameof(OutboundHttpPolicySettings.DeniedHosts) => s.DeniedHosts is null ? null : string.Join(", ", s.DeniedHosts),
        nameof(OutboundHttpPolicySettings.AllowedCidrs) => s.AllowedCidrs is null ? null : string.Join(", ", s.AllowedCidrs),
        nameof(OutboundHttpPolicySettings.DeniedCidrs) => s.DeniedCidrs is null ? null : string.Join(", ", s.DeniedCidrs),
        nameof(OutboundHttpPolicySettings.AllowCloudMetadata) => s.AllowCloudMetadata?.ToString(),
        _ => null,
    };

    private sealed class StoredDocument
    {
        public OutboundHttpOptions? Saved { get; set; }

        public List<OutboundHttpAuditEntry> Audit { get; set; } = new();
    }
}

/// <summary>🌐 Stage 7.3 — the setup screen's view of every channel.</summary>
/// <param name="Channels">Per-channel views.</param>
/// <param name="SavedDefault">The saved default layer.</param>
/// <param name="HostDefault">The host default layer (locked).</param>
/// <param name="UnconfiguredMode">What an unconfigured channel behaves as.</param>
public sealed record OutboundHttpSettingsView(
    IReadOnlyList<OutboundHttpChannelView> Channels,
    OutboundHttpPolicySettings? SavedDefault,
    OutboundHttpPolicySettings? HostDefault,
    OutboundHttpMode UnconfiguredMode);

/// <summary>🌐 Stage 7.3 — one channel in the setup screen.</summary>
/// <param name="Channel">The channel.</param>
/// <param name="Effective">The policy in force.</param>
/// <param name="Saved">The channel's saved layer.</param>
/// <param name="Host">The channel's host layer (locked).</param>
public sealed record OutboundHttpChannelView(
    OutboundHttpChannel Channel,
    EffectiveOutboundPolicy Effective,
    OutboundHttpPolicySettings? Saved,
    OutboundHttpPolicySettings? Host);

/// <summary>🌐 Stage 7.3 — one audit entry.</summary>
/// <param name="At">When.</param>
/// <param name="By">Who.</param>
/// <param name="Changes">What changed (<c>Scripts.Mode: PublicOnly → AllowlistOnly</c>…).</param>
public sealed record OutboundHttpAuditEntry(DateTimeOffset At, string By, IReadOnlyList<string> Changes);

/// <summary>🌐 Stage 7.3 — the outcome of "would this be allowed?".</summary>
/// <param name="Allowed">Whether the request would go through.</param>
/// <param name="Reason">The deciding rule.</param>
/// <param name="Addresses">Each resolved address and its outcome.</param>
public sealed record OutboundHttpCheckResult(bool Allowed, string Reason, IReadOnlyList<OutboundHttpAddressCheck> Addresses);

/// <summary>🌐 Stage 7.3 — one resolved address's outcome.</summary>
/// <param name="Address">The address.</param>
/// <param name="Allowed">Whether it's allowed.</param>
/// <param name="Reason">The deciding rule.</param>
public sealed record OutboundHttpAddressCheck(string Address, bool Allowed, string Reason);

/// <summary>🌐 Stage 7.3 — where the saved policy and its audit trail live.</summary>
public interface IOutboundHttpPolicyPersistence
{
    /// <summary>Reads the stored document (null = none yet).</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The JSON.</returns>
    Task<string?> ReadAsync(CancellationToken ct = default);

    /// <summary>Writes the stored document.</summary>
    /// <param name="json">The JSON.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task.</returns>
    Task WriteAsync(string json, CancellationToken ct = default);
}

/// <summary>🌐 Stage 7.3 — keeps the saved policy in the blob store, so every host in a cluster sees it.</summary>
public sealed class BlobOutboundHttpPolicyPersistence : IOutboundHttpPolicyPersistence
{
    private const string Key = "settings/outbound-http.json";
    private readonly IBlobStore blobs;

    /// <summary>Initializes a new instance of the <see cref="BlobOutboundHttpPolicyPersistence"/> class.</summary>
    /// <param name="blobs">The blob store.</param>
    public BlobOutboundHttpPolicyPersistence(IBlobStore blobs) => this.blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));

    /// <inheritdoc/>
    public async Task<string?> ReadAsync(CancellationToken ct = default)
    {
        await using var stream = await this.blobs.GetAsync(Key, ct).ConfigureAwait(false);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task WriteAsync(string json, CancellationToken ct = default)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await this.blobs.PutAsync(Key, stream, "application/json", ct).ConfigureAwait(false);
    }
}

/// <summary>🌐 Stage 7.3 — in-process persistence (no blob store; the policy lasts until restart).</summary>
public sealed class InMemoryOutboundHttpPolicyPersistence : IOutboundHttpPolicyPersistence
{
    private string? json;

    /// <inheritdoc/>
    public Task<string?> ReadAsync(CancellationToken ct = default) => Task.FromResult(this.json);

    /// <inheritdoc/>
    public Task WriteAsync(string json, CancellationToken ct = default)
    {
        this.json = json;
        return Task.CompletedTask;
    }
}

/// <summary>
/// 🌐 Stage 7.3 — loads the saved policy at startup and every minute (so a change saved on one host
/// reaches the others), and warns loudly about each channel nobody has configured.
/// </summary>
public sealed class OutboundHttpPolicyHostedService : BackgroundService
{
    private readonly OutboundHttpPolicyService service;
    private readonly ILogger<OutboundHttpPolicyHostedService> logger;

    /// <summary>Initializes a new instance of the <see cref="OutboundHttpPolicyHostedService"/> class.</summary>
    /// <param name="service">The policy service.</param>
    /// <param name="logger">The logger.</param>
    public OutboundHttpPolicyHostedService(OutboundHttpPolicyService service, ILogger<OutboundHttpPolicyHostedService> logger)
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
            this.logger.LogError(ex, "🌐 Couldn't load the saved outbound HTTP policy; using host settings only~");
        }

        foreach (var channel in OutboundHttpPolicyService.Channels)
        {
            var policy = this.service.Get(channel);
            if (!policy.Configured)
            {
                this.logger.LogWarning(
                    "🌐 Outbound HTTP for {Channel} isn't configured, so it runs as {Mode}: internal addresses (localhost, private networks) are blocked. " +
                    "Choose a mode under Settings → Outbound HTTP, or set {Section}:{Channel}:Mode~",
                    channel,
                    policy.Mode,
                    OutboundHttpOptions.SectionName,
                    channel);
            }
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
                    this.logger.LogWarning(ex, "🌐 Couldn't reload the outbound HTTP policy; keeping the last one~");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down~
        }
    }
}
