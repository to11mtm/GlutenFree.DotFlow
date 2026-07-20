// <copyright file="IDesignerCommand.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

/// <summary>
/// 📜 Phase 3.3.b — A reversible mutation against a <see cref="DesignerDocument"/> (D7). Every edit
/// in the designer is expressed as a command so undo/redo is uniform. Commands must be
/// self-contained: <see cref="Undo"/> restores exactly the state <see cref="Do"/> changed~ ✨.
/// </summary>
public interface IDesignerCommand
{
    /// <summary>A short human-readable description (shown in undo/redo tooltips)~ 📝.</summary>
    string Description { get; }

    /// <summary>Applies the mutation~ ▶️.</summary>
    /// <param name="document">The document to mutate.</param>
    void Do(DesignerDocument document);

    /// <summary>Reverses the mutation~ ◀️.</summary>
    /// <param name="document">The document to restore.</param>
    void Undo(DesignerDocument document);
}
