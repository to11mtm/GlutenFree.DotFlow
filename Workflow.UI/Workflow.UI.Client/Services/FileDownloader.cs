// <copyright file="FileDownloader.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Services;

using System.Threading.Tasks;
using Microsoft.JSInterop;

/// <summary>
/// 📥 Phase 3.6 (E1) — hands a generated file to the browser.
/// </summary>
/// <remarks>
/// An interface rather than a static call so component tests can assert "a download happened"
/// without a real browser — bUnit's JSInterop can verify the invocation, but a seam reads better at
/// the call site and keeps the components framework-free (D2)~ ✨.
/// </remarks>
public interface IFileDownloader
{
    /// <summary>Downloads text content as a file~ 📄.</summary>
    /// <param name="fileName">The suggested filename.</param>
    /// <param name="content">The file contents.</param>
    /// <param name="mimeType">The MIME type (defaults to JSON).</param>
    /// <returns>A task.</returns>
    ValueTask DownloadAsync(string fileName, string content, string mimeType = "application/json");
}

/// <summary>📥 The browser-backed <see cref="IFileDownloader"/>~ ✨.</summary>
public sealed class BrowserFileDownloader : IFileDownloader
{
    private readonly IJSRuntime js;

    /// <summary>Initializes a new instance of the <see cref="BrowserFileDownloader"/> class~ 📥.</summary>
    /// <param name="js">The JS runtime.</param>
    public BrowserFileDownloader(IJSRuntime js) => this.js = js;

    /// <inheritdoc/>
    public async ValueTask DownloadAsync(string fileName, string content, string mimeType = "application/json")
        => await this.js.InvokeVoidAsync("dotflowFiles.download", fileName, content, mimeType).ConfigureAwait(false);
}
