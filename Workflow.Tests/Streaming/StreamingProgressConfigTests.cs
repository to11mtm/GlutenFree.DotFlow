// <copyright file="StreamingProgressConfigTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Streaming;

using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Engine.Messages;
using Xunit;

/// <summary>
/// Stage 7.6 — the host registers how often a running streaming region pushes live item counts,
/// from <c>Streaming:ProgressIntervalMs</c> (default 1000 ms; 0 = final summary only).
/// </summary>
public sealed class StreamingProgressConfigTests
{
    [Theory]
    [InlineData(null, StreamingProgressOptions.DefaultIntervalMs)]
    [InlineData("250", 250)]
    [InlineData("0", 0)]
    public void TheHost_RegistersTheProgressInterval(string? setting, int expected)
    {
        using var factory = new Host(setting);
        factory.CreateClient();

        factory.Services.GetRequiredService<StreamingProgressOptions>().IntervalMs.Should().Be(expected);
    }

    /// <summary>⏱️ Stage 7.5 — region time limits: off unless the server sets them.</summary>
    [Theory]
    [InlineData(null, null, 0, 0)]
    [InlineData("300000", "3600000", 300000, 3600000)]
    public void TheHost_RegistersTheRegionTimeLimits(string? stall, string? total, int expectedStall, int expectedTotal)
    {
        using var factory = new Host(null, ("Streaming:RegionStallTimeoutMs", stall), ("Streaming:RegionTimeoutMs", total));
        factory.CreateClient();

        var options = factory.Services.GetRequiredService<StreamingTimeoutOptions>();
        options.StallTimeoutMs.Should().Be(expectedStall);
        options.TotalTimeoutMs.Should().Be(expectedTotal);
    }

    private sealed class Host : WebApplicationFactory<Program>
    {
        private readonly string? interval;
        private readonly (string Key, string? Value)[] extra;

        public Host(string? interval, params (string Key, string? Value)[] extra)
        {
            this.interval = interval;
            this.extra = extra;
        }

        // UseSetting: Program.cs reads the value while registering services.
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Persistence:ConnectionString", Workflow.Tests.TestSqlite.InMemory());
            if (this.interval is not null)
            {
                builder.UseSetting("Streaming:ProgressIntervalMs", this.interval);
            }

            foreach (var (key, value) in this.extra)
            {
                if (value is not null)
                {
                    builder.UseSetting(key, value);
                }
            }
        }
    }
}