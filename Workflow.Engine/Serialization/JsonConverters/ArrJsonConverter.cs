// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using System.Text.Json.Serialization;
using LanguageExt;

namespace Workflow.Engine.Serialization.JsonConverters;

/// <summary>
/// Custom JSON converter for LanguageExt Arr&lt;T&gt;.
/// Serializes as a standard JSON array and deserializes back to Arr~ 📚.
/// </summary>
/// <typeparam name="T">The type of elements in the Arr.</typeparam>
/// <remarks>
/// CopilotNote: While Arr serializes to JSON array correctly,
/// deserialization fails because Arr is read-only!
/// This converter handles both directions properly using the IEnumerable constructor~ ✨.
/// </remarks>
public class ArrJsonConverter<T> : JsonConverter<Arr<T>>
{
    /// <inheritdoc/>
    public override Arr<T> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return new Arr<T>();
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"Expected StartArray token, got {reader.TokenType}.");
        }

        var items = new List<T>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                break;
            }

            var item = JsonSerializer.Deserialize<T>(ref reader, options);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        // Build Arr using IEnumerable constructor 💖
        return new Arr<T>(items);
    }

    /// <inheritdoc/>
    public override void Write(
        Utf8JsonWriter writer,
        Arr<T> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        foreach (var item in value)
        {
            JsonSerializer.Serialize(writer, item, options);
        }

        writer.WriteEndArray();
    }
}

/// <summary>
/// Factory for creating ArrJsonConverter instances for any element type T.
/// Enables automatic Arr serialization for all element types~ 🏭.
/// </summary>
/// <remarks>
/// CopilotNote: Register this factory with JsonSerializerOptions and
/// all Arr&lt;T&gt; types will be handled automatically!.
/// </remarks>
public class ArrJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        if (!typeToConvert.IsGenericType)
        {
            return false;
        }

        return typeToConvert.GetGenericTypeDefinition() == typeof(Arr<>);
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var elementType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(ArrJsonConverter<>).MakeGenericType(elementType);

        return (JsonConverter?)Activator.CreateInstance(converterType);
    }
}
