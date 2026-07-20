// <copyright file="ToastService.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Services;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>🔔 Phase 3.3.a.1 — Toast severity levels~ ✨.</summary>
public enum ToastLevel
{
    /// <summary>Informational.</summary>
    Info,

    /// <summary>Success.</summary>
    Success,

    /// <summary>Warning.</summary>
    Warning,

    /// <summary>Error.</summary>
    Error,
}

/// <summary>🔔 Phase 3.3.a.1 — A single toast~ ✨.</summary>
/// <param name="Id">Unique id.</param>
/// <param name="Level">Severity.</param>
/// <param name="Message">The text.</param>
public sealed record Toast(Guid Id, ToastLevel Level, string Message);

/// <summary>
/// 🔔 Phase 3.3.a.1 — App-wide toast queue. Pages raise toasts; the <c>ToastHost</c> renders them.
/// Framework-free~ ✨.
/// </summary>
public sealed class ToastService
{
    private readonly List<Toast> toasts = new();

    /// <summary>Raised when the toast list changes~ 🔔.</summary>
    public event Action? Changed;

    /// <summary>Gets the current toasts (most-recent last)~ 📋.</summary>
    public IReadOnlyList<Toast> Toasts => this.toasts;

    /// <summary>Shows an info toast~ ℹ️.</summary>
    /// <param name="message">The message.</param>
    public void Info(string message) => this.Add(ToastLevel.Info, message);

    /// <summary>Shows a success toast~ ✅.</summary>
    /// <param name="message">The message.</param>
    public void Success(string message) => this.Add(ToastLevel.Success, message);

    /// <summary>Shows a warning toast~ ⚠️.</summary>
    /// <param name="message">The message.</param>
    public void Warning(string message) => this.Add(ToastLevel.Warning, message);

    /// <summary>Shows an error toast~ ❌.</summary>
    /// <param name="message">The message.</param>
    public void Error(string message) => this.Add(ToastLevel.Error, message);

    /// <summary>Dismisses a toast by id~ 🗑️.</summary>
    /// <param name="id">The toast id.</param>
    public void Dismiss(Guid id)
    {
        if (this.toasts.RemoveAll(t => t.Id == id) > 0)
        {
            this.Changed?.Invoke();
        }
    }

    private void Add(ToastLevel level, string message)
    {
        this.toasts.Add(new Toast(Guid.NewGuid(), level, message));
        this.Changed?.Invoke();
    }
}
