// <copyright file="TransformModuleException.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Transform.Internal;

using System;

/// <summary>
/// 🚨 Base exception for data-transformation module failures~ 🔄.
/// </summary>
public class TransformModuleException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TransformModuleException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="itemIndex">The index of the offending item, when applicable.</param>
    /// <param name="inner">The optional inner exception.</param>
    public TransformModuleException(string message, int? itemIndex = null, Exception? inner = null)
        : base(itemIndex is { } i ? $"{message} (item #{i})" : message, inner)
    {
        this.ItemIndex = itemIndex;
    }

    /// <summary>
    /// Gets the index of the offending item, when the failure is item-specific~ 🔢.
    /// </summary>
    public int? ItemIndex { get; }
}
