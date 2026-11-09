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

    private sealed class Host : WebApplicationFactory<Program>
    {
        private readonly string? interval;

        public Host(string? interval) => this.interval = interval;

        // UseSetting: Program.cs reads the value while registering services.
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Persistence:ConnectionString", Workflow.Tests.TestSqlite.InMemory());
            if (this.interval is not null)
            {
                builder.UseSetting("Streaming:ProgressIntervalMs", this.interval);
            }
        }
    }
}