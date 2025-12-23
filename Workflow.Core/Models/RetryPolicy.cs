// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

namespace Workflow.Core.Models;

/// <summary>
/// Defines retry behavior for a node when it encounters errors. 🔄
/// </summary>
/// <param name="MaxAttempts">Maximum number of retry attempts (including initial attempt). Default is 1 (no retries). 🎯</param>
/// <param name="DelayMs">Delay in milliseconds between retry attempts. Default is 1000ms (1 second). ⏱️</param>
/// <param name="BackoffMultiplier">Multiplier for exponential backoff. Set to 1.0 for fixed delay. Default is 2.0. 📈</param>
/// <param name="MaxDelayMs">Maximum delay in milliseconds for exponential backoff. Default is 60000ms (1 minute). ⏰</param>
/// <remarks>
/// CopilotNote: When BackoffMultiplier > 1.0, delay increases exponentially:
/// Attempt 1: DelayMs, Attempt 2: DelayMs * BackoffMultiplier, Attempt 3: DelayMs * BackoffMultiplier^2, etc.
/// The delay is capped at MaxDelayMs! Super smart, nya~! 💖
/// </remarks>
public record RetryPolicy(
	int MaxAttempts = 1,
	int DelayMs = 1000,
	double BackoffMultiplier = 2.0,
	int MaxDelayMs = 60000)
{
	/// <summary>
	/// Gets a retry policy with no retries (fail immediately). ❌
	/// </summary>
	public static RetryPolicy None => new(MaxAttempts: 1);

	/// <summary>
	/// Gets a retry policy with 3 attempts and exponential backoff. 🎀
	/// </summary>
	public static RetryPolicy Default => new(MaxAttempts: 3);

	/// <summary>
	/// Gets a retry policy with aggressive retries (5 attempts, longer delays). 💪
	/// </summary>
	public static RetryPolicy Aggressive => new(MaxAttempts: 5, DelayMs: 2000);
}

