// <copyright file="FileModuleException.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.File.Internal;

using System;

/// <summary>
/// 🚨 Base exception for file-system module failures~ 📁.
/// </summary>
public class FileModuleException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileModuleException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="inner">The optional inner exception.</param>
    public FileModuleException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// 🛡️ Thrown when a path fails the security sandbox (traversal, root escape,
/// blocked extension, symlink escape)~ 🚫.
/// </summary>
public sealed class PathSecurityException : FileModuleException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PathSecurityException"/> class.
    /// </summary>
    /// <param name="attemptedPath">The raw path the caller attempted to access.</param>
    /// <param name="reason">The reason the path was rejected.</param>
    public PathSecurityException(string attemptedPath, string reason)
        : base($"🛡️ Path rejected: {reason} (attempted: '{attemptedPath}')~ 🚫")
    {
        this.AttemptedPath = attemptedPath;
        this.Reason = reason;
    }

    /// <summary>
    /// Gets the raw path the caller attempted to access~ 📂.
    /// </summary>
    public string AttemptedPath { get; }

    /// <summary>
    /// Gets the reason the path was rejected~ 💬.
    /// </summary>
    public string Reason { get; }
}

/// <summary>
/// 🧠 Thrown when a file exceeds the configured maximum read size~ 📏.
/// </summary>
public sealed class FileTooLargeException : FileModuleException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileTooLargeException"/> class.
    /// </summary>
    /// <param name="actualBytes">The actual file size in bytes.</param>
    /// <param name="maxBytes">The configured maximum in bytes.</param>
    public FileTooLargeException(long actualBytes, long maxBytes)
        : base($"🧠 File is {actualBytes} bytes which exceeds the max of {maxBytes} bytes~ 📏")
    {
        this.ActualBytes = actualBytes;
        this.MaxBytes = maxBytes;
    }

    /// <summary>
    /// Gets the actual file size in bytes~ 📊.
    /// </summary>
    public long ActualBytes { get; }

    /// <summary>
    /// Gets the configured maximum in bytes~ 📏.
    /// </summary>
    public long MaxBytes { get; }
}
