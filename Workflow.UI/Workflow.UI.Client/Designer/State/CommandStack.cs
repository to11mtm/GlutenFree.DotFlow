// <copyright file="CommandStack.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;
using System.Collections.Generic;

/// <summary>
/// 📚 Phase 3.3.b — A bounded undo/redo stack over a <see cref="DesignerDocument"/> (D7). Executing
/// a command applies it, pushes it, and truncates any redo tail. Tracks a save-point for dirty
/// detection. Framework-free~ ✨.
/// </summary>
public sealed class CommandStack
{
    /// <summary>Maximum retained history entries (oldest dropped past this).</summary>
    public const int MaxDepth = 50;

    private readonly DesignerDocument document;
    private readonly List<IDesignerCommand> history = new();
    private int cursor; // number of applied commands (index just past the last applied)
    private int savePoint;

    /// <summary>Initializes a new instance of the <see cref="CommandStack"/> class~ 📚.</summary>
    /// <param name="document">The document commands act on.</param>
    public CommandStack(DesignerDocument document) => this.document = document;

    /// <summary>Raised after any stack change (execute/undo/redo/mark-saved)~ 🔔.</summary>
    public event Action? Changed;

    /// <summary>Gets a value indicating whether an undo is available.</summary>
    public bool CanUndo => this.cursor > 0;

    /// <summary>Gets a value indicating whether a redo is available.</summary>
    public bool CanRedo => this.cursor < this.history.Count;

    /// <summary>Gets a value indicating whether the document differs from the last save-point.</summary>
    public bool IsDirty => this.cursor != this.savePoint;

    /// <summary>The description of the next undo, or null~ 📝.</summary>
    public string? UndoDescription => this.CanUndo ? this.history[this.cursor - 1].Description : null;

    /// <summary>The description of the next redo, or null~ 📝.</summary>
    public string? RedoDescription => this.CanRedo ? this.history[this.cursor].Description : null;

    /// <summary>Executes a command, applies it, and records it~ ▶️.</summary>
    /// <param name="command">The command.</param>
    public void Execute(IDesignerCommand command)
    {
        command.Do(this.document);

        // Truncate the redo tail.
        if (this.cursor < this.history.Count)
        {
            this.history.RemoveRange(this.cursor, this.history.Count - this.cursor);

            // A truncated redo tail invalidates a save-point that lived in it.
            if (this.savePoint > this.cursor)
            {
                this.savePoint = -1;
            }
        }

        this.history.Add(command);
        this.cursor++;

        // Enforce the depth cap (drop oldest).
        if (this.history.Count > MaxDepth)
        {
            var drop = this.history.Count - MaxDepth;
            this.history.RemoveRange(0, drop);
            this.cursor -= drop;
            this.savePoint -= drop;
        }

        this.document.NotifyChanged();
        this.Changed?.Invoke();
    }

    /// <summary>Undoes the last command~ ◀️.</summary>
    public void Undo()
    {
        if (!this.CanUndo)
        {
            return;
        }

        this.cursor--;
        this.history[this.cursor].Undo(this.document);
        this.document.NotifyChanged();
        this.Changed?.Invoke();
    }

    /// <summary>Redoes the next command~ ▶️.</summary>
    public void Redo()
    {
        if (!this.CanRedo)
        {
            return;
        }

        this.history[this.cursor].Do(this.document);
        this.cursor++;
        this.document.NotifyChanged();
        this.Changed?.Invoke();
    }

    /// <summary>Marks the current position as saved (clears <see cref="IsDirty"/>)~ 💾.</summary>
    public void MarkSaved()
    {
        this.savePoint = this.cursor;
        this.Changed?.Invoke();
    }
}
