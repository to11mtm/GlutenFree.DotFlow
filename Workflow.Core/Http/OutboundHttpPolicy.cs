// <copyright file="OutboundHttpPolicy.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Core.Http;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 🌐 Stage 7.3 — which outbound HTTP a policy governs. Each channel has its own settings; an unset
/// channel inherits <see cref="OutboundHttpOptions.Default"/>. See <c>docs/outbound-http.md</c>.
/// </summary>
public enum OutboundHttpChannel
{
    /// <summary><c>workflow.httpGet/Post/Put/Delete</c> from JavaScript, Lua and C# scripts.</summary>
    Scripts,

    /// <summary>The HTTP Request module, including its OAuth2 token call.</summary>
    HttpModule,
}

/// <summary>🌐 Stage 7.3 — what a channel may reach (plus its allow/deny lists, where deny always wins).</summary>
public enum OutboundHttpMode
{
    /// <summary>Anything (cloud metadata still needs <c>AllowCloudMetadata</c>).</summary>
    Unrestricted,

    /// <summary>The public internet; internal addresses only when listed. The unconfigured default.</summary>
    PublicOnly,

    /// <summary>Internal addresses; the public internet only when listed.</summary>
    InternalOnly,

    /// <summary>Only listed hosts and address ranges.</summary>
    AllowlistOnly,
}

/// <summary>
/// 🌐 Stage 7.3 — one layer of settings for a channel (host settings, or the policy saved from the
/// setup screen). Every field is optional so layers can be merged and host values locked.
/// </summary>
public sealed class OutboundHttpPolicySettings
{
    /// <summary>Gets or sets the mode.</summary>
    public OutboundHttpMode? Mode { get; set; }

    /// <summary>Gets or sets allowed host names (exact, or <c>*.example.com</c>).</summary>
    public List<string>? AllowedHosts { get; set; }

    /// <summary>Gets or sets denied host names (exact, or <c>*.example.com</c>). Deny always wins.</summary>
    public List<string>? DeniedHosts { get; set; }

    /// <summary>Gets or sets allowed address ranges (<c>10.20.0.0/16</c>, <c>2001:db8::/32</c>, or a single address).</summary>
    public List<string>? AllowedCidrs { get; set; }

    /// <summary>Gets or sets denied address ranges. Deny always wins.</summary>
    public List<string>? DeniedCidrs { get; set; }

    /// <summary>
    /// Gets or sets whether cloud metadata endpoints (<c>169.254.169.254</c>, <c>fd00:ec2::254</c>,
    /// <c>metadata.google.internal</c>) may be reached. Default false — in every mode, including
    /// Unrestricted; listing them in the allow lists isn't enough.
    /// </summary>
    public bool? AllowCloudMetadata { get; set; }

    /// <summary>The names of the settable fields (for locking / auditing).</summary>
    public static readonly IReadOnlyList<string> FieldNames =
        [nameof(Mode), nameof(AllowedHosts), nameof(DeniedHosts), nameof(AllowedCidrs), nameof(DeniedCidrs), nameof(AllowCloudMetadata)];

    /// <summary>The fields this layer sets.</summary>
    /// <returns>The field names.</returns>
    public IEnumerable<string> SetFields()
    {
        if (this.Mode is not null) { yield return nameof(this.Mode); }
        if (this.AllowedHosts is not null) { yield return nameof(this.AllowedHosts); }
        if (this.DeniedHosts is not null) { yield return nameof(this.DeniedHosts); }
        if (this.AllowedCidrs is not null) { yield return nameof(this.AllowedCidrs); }
        if (this.DeniedCidrs is not null) { yield return nameof(this.DeniedCidrs); }
        if (this.AllowCloudMetadata is not null) { yield return nameof(this.AllowCloudMetadata); }
    }
}

/// <summary>🌐 Stage 7.3 — the <c>Http:Outbound</c> host settings: a default plus per-channel overrides.</summary>
public sealed class OutboundHttpOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Http:Outbound";

    /// <summary>Gets or sets settings every channel inherits.</summary>
    public OutboundHttpPolicySettings? Default { get; set; }

    /// <summary>Gets or sets the scripts channel's settings.</summary>
    public OutboundHttpPolicySettings? Scripts { get; set; }

    /// <summary>Gets or sets the HTTP Request module channel's settings.</summary>
    public OutboundHttpPolicySettings? HttpModule { get; set; }

    /// <summary>Gets a channel's own settings (null when unset).</summary>
    /// <param name="channel">The channel.</param>
    /// <returns>The settings.</returns>
    public OutboundHttpPolicySettings? For(OutboundHttpChannel channel)
        => channel == OutboundHttpChannel.Scripts ? this.Scripts : this.HttpModule;
}

/// <summary>🌐 Stage 7.3 — a channel's resolved policy.</summary>
/// <param name="Channel">The channel.</param>
/// <param name="Mode">The mode in force.</param>
/// <param name="Configured">False when no layer set a mode (then <see cref="Mode"/> is the fail-closed PublicOnly).</param>
/// <param name="AllowedHosts">Allowed host patterns.</param>
/// <param name="DeniedHosts">Denied host patterns.</param>
/// <param name="AllowedCidrs">Allowed address ranges.</param>
/// <param name="DeniedCidrs">Denied address ranges.</param>
/// <param name="AllowCloudMetadata">Whether metadata endpoints may be reached.</param>
/// <param name="LockedFields">Fields set by host settings (read-only in the setup screen).</param>
public sealed record EffectiveOutboundPolicy(
    OutboundHttpChannel Channel,
    OutboundHttpMode Mode,
    bool Configured,
    IReadOnlyList<string> AllowedHosts,
    IReadOnlyList<string> DeniedHosts,
    IReadOnlyList<string> AllowedCidrs,
    IReadOnlyList<string> DeniedCidrs,
    bool AllowCloudMetadata,
    IReadOnlyCollection<string> LockedFields)
{
    /// <summary>The fail-closed default for an unconfigured channel (decided 2026-11-09).</summary>
    public const OutboundHttpMode UnconfiguredMode = OutboundHttpMode.PublicOnly;

    /// <summary>A policy allowing anything except cloud metadata (tests, local tools).</summary>
    /// <param name="channel">The channel.</param>
    /// <returns>The policy.</returns>
    public static EffectiveOutboundPolicy Unrestricted(OutboundHttpChannel channel)
        => new(channel, OutboundHttpMode.Unrestricted, true, [], [], [], [], false, []);

    /// <summary>
    /// Merges the layers. Per field, the first that sets it wins: host channel, host default, saved
    /// channel, saved default. Fields set by either host layer are locked.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="host">Host settings (appsettings / environment).</param>
    /// <param name="saved">The policy saved from the setup screen (may be null).</param>
    /// <returns>The effective policy.</returns>
    public static EffectiveOutboundPolicy Resolve(OutboundHttpChannel channel, OutboundHttpOptions? host, OutboundHttpOptions? saved)
    {
        var layers = new[] { host?.For(channel), host?.Default, saved?.For(channel), saved?.Default }
            .Where(l => l is not null)
            .Select(l => l!)
            .ToList();

        T? First<T>(Func<OutboundHttpPolicySettings, T?> pick)
            where T : class
            => layers.Select(pick).FirstOrDefault(v => v is not null);

        var mode = layers.Select(l => l.Mode).FirstOrDefault(m => m is not null);
        var locked = new[] { host?.For(channel), host?.Default }
            .Where(l => l is not null)
            .SelectMany(l => l!.SetFields())
            .ToHashSet(StringComparer.Ordinal);

        return new EffectiveOutboundPolicy(
            channel,
            mode ?? UnconfiguredMode,
            mode is not null,
            First(l => l.AllowedHosts) ?? [],
            First(l => l.DeniedHosts) ?? [],
            First(l => l.AllowedCidrs) ?? [],
            First(l => l.DeniedCidrs) ?? [],
            layers.Select(l => l.AllowCloudMetadata).FirstOrDefault(v => v is not null) ?? false,
            locked);
    }
}

/// <summary>🌐 Stage 7.3 — supplies the policy in force for a channel (host settings + setup screen).</summary>
public interface IOutboundHttpPolicyProvider
{
    /// <summary>Gets the effective policy for a channel.</summary>
    /// <param name="channel">The channel.</param>
    /// <returns>The policy.</returns>
    EffectiveOutboundPolicy Get(OutboundHttpChannel channel);
}

/// <summary>🌐 Stage 7.3 — a provider over fixed settings (no saved policy). Unset = fail-closed PublicOnly.</summary>
public sealed class StaticOutboundHttpPolicyProvider : IOutboundHttpPolicyProvider
{
    private readonly Func<OutboundHttpChannel, EffectiveOutboundPolicy> resolve;

    /// <summary>Initializes a new instance of the <see cref="StaticOutboundHttpPolicyProvider"/> class.</summary>
    /// <param name="host">The settings (null = unconfigured).</param>
    public StaticOutboundHttpPolicyProvider(OutboundHttpOptions? host)
        => this.resolve = channel => EffectiveOutboundPolicy.Resolve(channel, host, null);

    private StaticOutboundHttpPolicyProvider(Func<OutboundHttpChannel, EffectiveOutboundPolicy> resolve) => this.resolve = resolve;

    /// <summary>Gets a provider that allows anything except cloud metadata (tests, local tools).</summary>
    public static StaticOutboundHttpPolicyProvider Unrestricted { get; } = new(EffectiveOutboundPolicy.Unrestricted);

    /// <inheritdoc/>
    public EffectiveOutboundPolicy Get(OutboundHttpChannel channel) => this.resolve(channel);
}
