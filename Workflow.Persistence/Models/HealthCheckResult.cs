// <copyright file="HealthCheckResult.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Models;

/// <summary>
/// 🏥 Result of a persistence provider health check~ ✨
/// </summary>
public record HealthCheckResult(
    bool IsHealthy,
    string ProviderName,
    TimeSpan Latency,
    string? ErrorMessage = null,
    IReadOnlyDictionary<string, object?>? Details = null);

