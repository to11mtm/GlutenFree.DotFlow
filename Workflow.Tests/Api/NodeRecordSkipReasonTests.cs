// <copyright file="NodeRecordSkipReasonTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api;

using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Workflow.Api.Contracts;
using Workflow.Core.Models;
using Workflow.Persistence.Models;
using Xunit;

/// <summary>
/// Stage 7.8 — a history record's <c>skipReason</c> metadata (written for a streaming stage skipped
/// because an earlier stage failed) reaches API clients as <c>skipReason</c>, whether the store
/// hands it back as a string or as JSON.
/// </summary>
public sealed class NodeRecordSkipReasonTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheSkipReason_IsMappedFromTheRecordsMetadata(bool asJson)
    {
        object? value = asJson
            ? JsonSerializer.SerializeToElement("skipped: upstream stage 'boom' failed")
            : "skipped: upstream stage 'boom' failed";
        var record = new NodeExecutionRecord(
            Guid.NewGuid(), "collect", NodeExecutionState.Skipped, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            null, null, null, TimeSpan.Zero, null, null,
            Metadata: new Dictionary<string, object?> { ["skipReason"] = value });

        NodeExecutionRecordDto.From(record).SkipReason.Should().Be("skipped: upstream stage 'boom' failed");
    }

    [Fact]
    public void ARecordWithoutOne_HasNoSkipReason()
    {
        var record = new NodeExecutionRecord(
            Guid.NewGuid(), "n1", NodeExecutionState.Completed, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            null, null, null, TimeSpan.Zero, null, null);

        NodeExecutionRecordDto.From(record).SkipReason.Should().BeNull();
    }
}
