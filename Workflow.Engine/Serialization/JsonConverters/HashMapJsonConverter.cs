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
/// Custom JSON converter for LanguageExt HashMap&lt;TKey, TValue&gt;.
/// Serializes as a JSON object {"key1":"value1",...} and deserializes back to HashMap~ 🗺️
/// </summary>
/// <typeparam name="TKey">The type of keys in the HashMap.</typeparam>
/// <typeparam name="TValue">The type of values in the HashMap.</typeparam>
/// <remarks>
/// CopilotNote: This converter is required because System.Text.Json cannot properly
/// serialize/deserialize LanguageExt's HashMap type out of the box!
/// The default serialization produces an array of objects which loses key-value semantics.
/// We serialize it as a proper JSON object for human-readable APIs~ ✨
/// </remarks>
public class HashMapJsonConverter<TKey, TValue> : JsonConverter<HashMap<TKey, TValue>>
    where TKey : notnull
{
    /// <inheritdoc/>
    public override HashMap<TKey, TValue> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return new HashMap<TKey, TValue>();
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected StartObject token, got {reader.TokenType}.");
        }

        var items = new List<(TKey Key, TValue Value)>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException($"Expected PropertyName, got {reader.TokenType}.");
            }

            // Read the key - convert from string to TKey type
            var keyString = reader.GetString();
            if (keyString is null)
            {
                throw new JsonException("HashMap key cannot be null.");
            }

            var key = ConvertKey(keyString);

            // Read the value
            reader.Read();
            var value = JsonSerializer.Deserialize<TValue>(ref reader, options);

            items.Add((key, value!));
        }

        // Build HashMap using IEnumerable constructor pattern 💖
        return toHashMap(items);
    }

    /// <inheritdoc/>
    public override void Write(
        Utf8JsonWriter writer,
        HashMap<TKey, TValue> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var (key, val) in value)
        {
            // Convert key to string for JSON property name
            var keyString = key?.ToString() ?? string.Empty;
            writer.WritePropertyName(keyString);
            JsonSerializer.Serialize(writer, val, options);
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Converts a string key to the target key type.
    /// Supports common key types like string, Guid, int, etc~ 🔑
    /// </summary>
    private static TKey ConvertKey(string keyString)
    {
        var keyType = typeof(TKey);

        // String is the most common case 🎀
        if (keyType == typeof(string))
        {
            return (TKey)(object)keyString;
        }

        // Guid support for workflow IDs and such 🆔
        if (keyType == typeof(Guid))
        {
            return (TKey)(object)Guid.Parse(keyString);
        }

        // Integer keys 🔢
        if (keyType == typeof(int))
        {
            return (TKey)(object)int.Parse(keyString);
        }

        if (keyType == typeof(long))
        {
            return (TKey)(object)long.Parse(keyString);
        }

        // Try generic conversion as fallback ✨
        return (TKey)Convert.ChangeType(keyString, keyType);
    }
}

/// <summary>
/// Factory for creating HashMapJsonConverter instances for any key-value type combination.
/// This allows the converter to be registered generically for all HashMap types~ 🏭
/// </summary>
/// <remarks>
/// CopilotNote: Register this factory with JsonSerializerOptions to enable
/// automatic HashMap serialization for any TKey/TValue combination!
/// </remarks>
public class HashMapJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        if (!typeToConvert.IsGenericType)
        {
            return false;
        }

        return typeToConvert.GetGenericTypeDefinition() == typeof(HashMap<,>);
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var keyType = typeToConvert.GetGenericArguments()[0];
        var valueType = typeToConvert.GetGenericArguments()[1];

        var converterType = typeof(HashMapJsonConverter<,>).MakeGenericType(keyType, valueType);

        return (JsonConverter?)Activator.CreateInstance(converterType);
    }
}
