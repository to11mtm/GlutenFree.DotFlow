// <copyright file="NoopFileDownloader.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Components;

using System.Collections.Generic;
using System.Threading.Tasks;
using Workflow.UI.Client.Services;

/// <summary>
/// 📥 A test double for <see cref="IFileDownloader"/> that records downloads instead of handing
/// them to a browser~ ✨.
/// </summary>
internal sealed class NoopFileDownloader : IFileDownloader
{
    /// <summary>Every download this instance was asked to perform.</summary>
    public List<(string FileName, string Content)> Downloads { get; } = new();

    /// <inheritdoc/>
    public ValueTask DownloadAsync(string fileName, string content, string mimeType = "application/json")
    {
        this.Downloads.Add((fileName, content));
        return ValueTask.CompletedTask;
    }
}
