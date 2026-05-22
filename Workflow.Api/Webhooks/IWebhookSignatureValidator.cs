// <copyright file="IWebhookSignatureValidator.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Webhooks;

using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Workflow.Core.Models;

// =============================================================================
// 🔒 Phase 2.3.7 — Webhook Signature Validation
//
// Architecture:
//   IWebhookSignatureValidator — one impl per scheme ("hmac-sha256" | "github" | "stripe")
//   WebhookSignatureValidatorRegistry — static lookup; new schemes register here~
//
// Forward-compat notes:
//   All validators receive the full WebhookRegistration so future schemes can read
//   custom per-registration config (e.g. timestamp tolerance, header name override)
//   without changing the interface signature~ 🧠
// =============================================================================

/// <summary>
/// 🔒 Phase 2.3.7 — Validates the cryptographic signature of an inbound webhook request~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// Implementations are stateless and threadsafe — the same instance is reused across all requests.
/// </para>
/// <para>
/// CopilotNote: The <paramref name="rawBody"/> must be the exact bytes received off the wire,
/// BEFORE any JSON parsing — hash functions are computed over the raw bytes, not the deserialised
/// object graph~ 🔒
/// </para>
/// </remarks>
public interface IWebhookSignatureValidator
{
    /// <summary>The scheme name this validator handles (e.g. <c>"github"</c>, <c>"stripe"</c>).</summary>
    string SchemeName { get; }

    /// <summary>
    /// Validate the cryptographic signature on an inbound request~ 🔐.
    /// </summary>
    /// <param name="headers">Inbound request headers.</param>
    /// <param name="rawBody">Raw request body bytes (pre-JSON parse).</param>
    /// <param name="secretKey">The secret registered with the webhook.</param>
    /// <param name="registration">Full registration (for scheme-specific config).</param>
    /// <returns>A <see cref="SignatureValidationResult"/> indicating pass or failure.</returns>
    SignatureValidationResult Validate(
        IHeaderDictionary headers,
        byte[] rawBody,
        string secretKey,
        WebhookRegistration registration);
}

// =============================================================================
// 🔒 Validation result
// =============================================================================

/// <summary>Result of a signature validation attempt~ 🔒.</summary>
/// <remarks>
/// CopilotNote: Always use <see cref="Valid"/> / <see cref="Invalid"/> factory methods —
/// never construct directly. The <see cref="FailureReason"/> is logged but NOT returned to the
/// caller in the HTTP response (to avoid oracle attacks)~ 🛡️
/// </remarks>
public sealed record SignatureValidationResult
{
    private SignatureValidationResult(bool isValid, string? failureReason)
    {
        IsValid = isValid;
        FailureReason = failureReason;
    }

    /// <summary>Whether the signature was valid.</summary>
    public bool IsValid { get; }

    /// <summary>Human-readable reason for failure (only set when <see cref="IsValid"/> is false).</summary>
    public string? FailureReason { get; }

    /// <summary>Creates a valid result~ ✅.</summary>
    public static SignatureValidationResult Valid() => new(true, null);

    /// <summary>Creates an invalid result with an explanatory <paramref name="reason"/>~ ❌.</summary>
    public static SignatureValidationResult Invalid(string reason) => new(false, reason);
}

// =============================================================================
// 🔐 HmacSha256SignatureValidator — generic HMAC-SHA256 scheme~
// =============================================================================

/// <summary>
/// Generic HMAC-SHA256 validator~ 🔐.
/// Reads the raw lowercase hex digest from the <c>X-Signature</c> header and compares against
/// <c>HMAC-SHA256(secretKey, rawBody)</c>.
/// </summary>
/// <remarks>
/// <para>
/// Header format: <c>X-Signature: {lowercase-hex}</c>
/// </para>
/// <para>
/// CopilotNote: Uses <see cref="CryptographicOperations.FixedTimeEquals"/> to prevent timing
/// attacks. Never use <c>string.Equals</c> for signature comparison~ 🛡️
/// </para>
/// </remarks>
public sealed class HmacSha256SignatureValidator : IWebhookSignatureValidator
{
    /// <summary>Default header name for the generic HMAC-SHA256 scheme~ 📋.</summary>
    public const string DefaultHeaderName = "X-Signature";

    /// <inheritdoc/>
    public string SchemeName => "hmac-sha256";

    /// <inheritdoc/>
    public SignatureValidationResult Validate(
        IHeaderDictionary headers,
        byte[] rawBody,
        string secretKey,
        WebhookRegistration registration)
    {
        if (!headers.TryGetValue(DefaultHeaderName, out var headerValue)
            || string.IsNullOrEmpty(headerValue))
        {
            return SignatureValidationResult.Invalid(
                $"Missing required header '{DefaultHeaderName}'.");
        }

        var expected = ComputeHmacSha256Hex(secretKey, rawBody);
        return FixedTimeEquals(expected, headerValue.ToString())
            ? SignatureValidationResult.Valid()
            : SignatureValidationResult.Invalid("Signature mismatch.");
    }

    // -------------------------------------------------------------------------
    // Internal helpers reused by GitHub + Stripe validators~ 🔧
    // -------------------------------------------------------------------------

    /// <summary>
    /// Compute HMAC-SHA256 over <paramref name="data"/> using <paramref name="secretKey"/>
    /// and return the lowercase hex string~ 🔧.
    /// </summary>
    internal static string ComputeHmacSha256Hex(string secretKey, byte[] data)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secretKey);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Constant-time string equality — prevents timing oracle attacks~ 🛡️.
    /// </summary>
    internal static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a),
            Encoding.UTF8.GetBytes(b));
}

// =============================================================================
// 🐙 GitHubSignatureValidator — X-Hub-Signature-256: sha256={hex}~
// =============================================================================

/// <summary>
/// GitHub webhook signature validator~ 🐙.
/// Reads <c>X-Hub-Signature-256: sha256={hex}</c> and validates against
/// <c>HMAC-SHA256(secretKey, rawBody)</c>.
/// </summary>
/// <remarks>
/// <para>Reference: <see href="https://docs.github.com/en/webhooks/using-webhooks/validating-webhook-deliveries"/></para>
/// <para>CopilotNote: The <c>sha256=</c> prefix is mandatory — validation fails immediately if absent~ 🔒</para>
/// </remarks>
public sealed class GitHubSignatureValidator : IWebhookSignatureValidator
{
    /// <summary>Header name used by GitHub webhook deliveries.</summary>
    public const string HeaderName = "X-Hub-Signature-256";

    private const string Prefix = "sha256=";

    /// <inheritdoc/>
    public string SchemeName => "github";

    /// <inheritdoc/>
    public SignatureValidationResult Validate(
        IHeaderDictionary headers,
        byte[] rawBody,
        string secretKey,
        WebhookRegistration registration)
    {
        if (!headers.TryGetValue(HeaderName, out var headerValue)
            || string.IsNullOrEmpty(headerValue))
        {
            return SignatureValidationResult.Invalid(
                $"Missing required header '{HeaderName}'.");
        }

        var raw = headerValue.ToString();
        if (!raw.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return SignatureValidationResult.Invalid(
                $"Header '{HeaderName}' must start with '{Prefix}'.");
        }

        var receivedHex = raw[Prefix.Length..];
        var expected = HmacSha256SignatureValidator.ComputeHmacSha256Hex(secretKey, rawBody);
        return HmacSha256SignatureValidator.FixedTimeEquals(expected, receivedHex)
            ? SignatureValidationResult.Valid()
            : SignatureValidationResult.Invalid("GitHub signature mismatch.");
    }
}

// =============================================================================
// 💳 StripeSignatureValidator — Stripe-Signature: t={unix},v1={hex}~
// =============================================================================

/// <summary>
/// Stripe webhook signature validator with replay-attack protection~ 💳.
/// Parses <c>Stripe-Signature: t={unix-timestamp},v1={hex}</c> and validates against
/// <c>HMAC-SHA256(secretKey, "{timestamp}.{rawBody}")</c>.
/// Rejects events older than <see cref="DefaultTolerance"/> (default 5 minutes)~ ⏰.
/// </summary>
/// <remarks>
/// <para>Reference: <see href="https://stripe.com/docs/webhooks#verify-official-libraries"/></para>
/// <para>
/// CopilotNote: Replay protection checks the WALL-CLOCK age of the event, NOT the age of the
/// request. If your host clocks are skewed, set a wider tolerance. The workflow does NOT get
/// triggered on replay rejection — the dispatcher returns 401 before LaunchAsync is called~ 🛡️
/// </para>
/// </remarks>
public sealed class StripeSignatureValidator : IWebhookSignatureValidator
{
    /// <summary>Header name used by Stripe webhook deliveries.</summary>
    public const string HeaderName = "Stripe-Signature";

    /// <summary>Default replay-protection window (5 minutes)~ ⏰.</summary>
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    private readonly TimeSpan _tolerance;

    /// <summary>Initialises the validator, optionally overriding the timestamp tolerance~ 🎀.</summary>
    public StripeSignatureValidator(TimeSpan? tolerance = null)
    {
        _tolerance = tolerance ?? DefaultTolerance;
    }

    /// <inheritdoc/>
    public string SchemeName => "stripe";

    /// <inheritdoc/>
    public SignatureValidationResult Validate(
        IHeaderDictionary headers,
        byte[] rawBody,
        string secretKey,
        WebhookRegistration registration)
    {
        if (!headers.TryGetValue(HeaderName, out var headerValue)
            || string.IsNullOrEmpty(headerValue))
        {
            return SignatureValidationResult.Invalid(
                $"Missing required header '{HeaderName}'.");
        }

        // Parse: "t=<unix>,v1=<hex>[,v0=<deprecated>]"
        long? timestamp = null;
        string? v1Sig = null;

        foreach (var part in headerValue.ToString().Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("t=", StringComparison.Ordinal)
                && long.TryParse(trimmed[2..], out var ts))
            {
                timestamp = ts;
            }
            else if (trimmed.StartsWith("v1=", StringComparison.Ordinal))
            {
                v1Sig = trimmed[3..];
            }
        }

        if (timestamp is null)
        {
            return SignatureValidationResult.Invalid(
                "Missing 't' (timestamp) field in Stripe-Signature header.");
        }

        if (v1Sig is null)
        {
            return SignatureValidationResult.Invalid(
                "Missing 'v1' signature field in Stripe-Signature header.");
        }

        // ⏰ Replay protection~ — reject if event is outside the tolerance window
        var eventTime = DateTimeOffset.FromUnixTimeSeconds(timestamp.Value);
        var age = DateTimeOffset.UtcNow - eventTime;
        if (age > _tolerance)
        {
            return SignatureValidationResult.Invalid(
                $"Stripe timestamp is too old ({age.TotalSeconds:F0}s > tolerance {_tolerance.TotalSeconds:F0}s). Possible replay attack~ 🛡️");
        }

        // 🔐 Signed payload: "{timestamp}.{rawBodyUtf8}"
        var prefixBytes = Encoding.UTF8.GetBytes($"{timestamp}.");
        var signedPayload = new byte[prefixBytes.Length + rawBody.Length];
        prefixBytes.CopyTo(signedPayload, 0);
        rawBody.CopyTo(signedPayload, prefixBytes.Length);

        var expected = HmacSha256SignatureValidator.ComputeHmacSha256Hex(secretKey, signedPayload);
        return HmacSha256SignatureValidator.FixedTimeEquals(expected, v1Sig)
            ? SignatureValidationResult.Valid()
            : SignatureValidationResult.Invalid("Stripe v1 signature mismatch.");
    }
}

// =============================================================================
// 🗂️ WebhookSignatureValidatorRegistry — static resolver~
// =============================================================================

/// <summary>
/// 🗂️ Phase 2.3.7 — Resolves an <see cref="IWebhookSignatureValidator"/> by scheme name~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// Built-in schemes: <c>"hmac-sha256"</c>, <c>"github"</c>, <c>"stripe"</c>.
/// </para>
/// <para>
/// CopilotNote: This is a static registry (no DI) because validators are pure, stateless, and
/// deterministic — there's no practical benefit to injection for V1. If custom validators are
/// needed in future, the registry can be promoted to an <c>IWebhookSignatureValidatorRegistry</c>
/// DI service~ 🧠
/// </para>
/// </remarks>
public static class WebhookSignatureValidatorRegistry
{
    private static readonly IWebhookSignatureValidator[] BuiltIn =
    [
        new HmacSha256SignatureValidator(),
        new GitHubSignatureValidator(),
        new StripeSignatureValidator(),
    ];

    /// <summary>
    /// Returns the built-in validator for <paramref name="schemeName"/>, or
    /// <see langword="null"/> if no validator is registered for that scheme~ 🔍.
    /// </summary>
    public static IWebhookSignatureValidator? Resolve(string schemeName) =>
        Array.Find(BuiltIn, v =>
            string.Equals(v.SchemeName, schemeName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="schemeName"/> matches a known
    /// built-in validator~ ✅.
    /// </summary>
    /// <remarks>
    /// Used at registration time to reject unknown schemes before they can silently
    /// bypass signature validation at trigger time~ 🛡️
    /// </remarks>
    public static bool IsKnownScheme(string schemeName) =>
        Resolve(schemeName) is not null;
}

