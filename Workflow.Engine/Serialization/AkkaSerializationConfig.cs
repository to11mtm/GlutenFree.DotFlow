// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using MessagePack;

namespace Workflow.Engine.Serialization;

/// <summary>
/// Provides HOCON configuration and MessagePack options for Akka.NET serialization bindings.
/// Delegates to <see cref="MsgPack2Setup"/> for the actual configuration.
/// </summary>
/// <remarks>
/// CopilotNote: This class is kept for backward compatibility.
/// For new code, prefer using <see cref="MsgPack2Setup"/> directly.
/// </remarks>
[Obsolete("Use MsgPack2Setup instead for new code.")]
public static class AkkaSerializationConfig
{
    /// <summary>
    /// Gets the MessagePackSerializerOptions configured with LanguageExt type support.
    /// </summary>
    public static MessagePackSerializerOptions WorkflowMessagePackOptions =>
        MsgPack2Setup.WorkflowOptions;

    /// <summary>
    /// Gets the HOCON configuration string for workflow serialization.
    /// </summary>
    /// <returns>HOCON configuration string for Akka.NET serialization.</returns>
    public static string GetSerializationHocon() =>
        MsgPack2Setup.GetSerializationHocon();

    /// <summary>
    /// Gets a minimal HOCON configuration for testing.
    /// </summary>
    /// <returns>HOCON configuration string for test environments.</returns>
    public static string GetTestHocon() =>
        MsgPack2Setup.GetTestHocon();

    /// <summary>
    /// Gets the full HOCON configuration for production use.
    /// </summary>
    /// <returns>HOCON configuration string with full serializer setup.</returns>
    public static string GetFullSerializationHocon() =>
        MsgPack2Setup.GetSerializationHocon();
}
