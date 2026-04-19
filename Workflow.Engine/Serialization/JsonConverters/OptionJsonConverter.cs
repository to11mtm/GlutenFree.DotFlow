// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using System.Text.Json.Serialization;
using LanguageExt;
using static LanguageExt.Prelude;

namespace Workflow.Engine.Serialization.JsonConverters;

/// <summary>
/// Custom JSON converter for LanguageExt Option&lt;T&gt;.
/// Serializes Some(value) as the value itself, and None as null~ 💫.
/// </summary>
/// <typeparam name="T">The type wrapped by Option.</typeparam>
/// <remarks>
/// CopilotNote: System.Text.Json serializes Option as an array ["value"] by default,
/// which is not useful for REST APIs! We want:
/// - Some("hello") → "hello"
/// - None → null
/// Much more intuitive for API consumers~ 🎀.
/// </remarks>
public class OptionJsonConverter<T> : JsonConverter<Option<T>>
{
    /// <inheritdoc/>
    public override Option<T> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        // Null represents None 🚫
        if (reader.TokenType == JsonTokenType.Null)
        {
            return None;
        }

        // Any other value represents Some(value) 💖
        var value = JsonSerializer.Deserialize<T>(ref reader, options);
        return value is null ? None : Some(value);
    }

    /// <inheritdoc/>
    public override void Write(
        Utf8JsonWriter writer,
        Option<T> value,
        JsonSerializerOptions options)
    {
        value.Match(
            Some: v => JsonSerializer.Serialize(writer, v, options),
            None: () => writer.WriteNullValue());
    }
}

/// <summary>
/// Factory for creating OptionJsonConverter instances for any type T.
/// Enables automatic Option serialization for all wrapped types~ 🏭.
/// </summary>
/// <remarks>
/// CopilotNote: Register this factory with JsonSerializerOptions and
/// all Option&lt;T&gt; types will be handled automatically!.
/// </remarks>
public class OptionJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        if (!typeToConvert.IsGenericType)
        {
            return false;
        }

        return typeToConvert.GetGenericTypeDefinition() == typeof(Option<>);
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var innerType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(OptionJsonConverter<>).MakeGenericType(innerType);

        return (JsonConverter?)Activator.CreateInstance(converterType);
    }
}
