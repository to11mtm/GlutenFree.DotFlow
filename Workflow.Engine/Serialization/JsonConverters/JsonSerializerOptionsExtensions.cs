// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Workflow.Engine.Serialization.JsonConverters;

/// <summary>
/// Extension methods for configuring JsonSerializerOptions with LanguageExt converters.
/// Makes it super easy to register all the kawaii converters~ 💖.
/// </summary>
/// <remarks>
/// CopilotNote: Use these extensions to configure System.Text.Json for REST APIs,
/// SignalR, and any other external communication that needs human-readable JSON.
/// MessagePack works out of the box for Akka.NET internal serialization!.
/// </remarks>
public static class JsonSerializerOptionsExtensions
{
    /// <summary>
    /// Adds all LanguageExt type converters to the JsonSerializerOptions.
    /// This enables proper serialization of HashMap, Option, and Arr types~ ✨.
    /// </summary>
    /// <param name="options">The JsonSerializerOptions to configure.</param>
    /// <returns>The same options instance for fluent chaining.</returns>
    /// <example>
    /// <code>
    /// var options = new JsonSerializerOptions()
    ///     .AddLanguageExtConverters();
    /// </code>
    /// </example>
    public static JsonSerializerOptions AddLanguageExtConverters(this JsonSerializerOptions options)
    {
        options.Converters.Add(new HashMapJsonConverterFactory());
        options.Converters.Add(new OptionJsonConverterFactory());
        options.Converters.Add(new ArrJsonConverterFactory());
        return options;
    }

    /// <summary>
    /// Creates a new JsonSerializerOptions instance pre-configured for workflow serialization.
    /// Includes all LanguageExt converters and sensible defaults for APIs~ 🎀.
    /// </summary>
    /// <param name="writeIndented">Whether to write indented JSON (default: false for production).</param>
    /// <returns>A configured JsonSerializerOptions instance.</returns>
    public static JsonSerializerOptions CreateWorkflowJsonOptions(bool writeIndented = false)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = writeIndented,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
        };

        options.AddLanguageExtConverters();

        return options;
    }
}
