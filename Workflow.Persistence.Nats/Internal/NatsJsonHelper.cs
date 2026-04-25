// <copyright file="NatsJsonHelper.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Nats.Internal;

using System.Text.Json;
using System.Text.Json.Serialization;
using LanguageExt;

/// <summary>
/// 🔧 JSON serialisation helpers shared across NATS repositories~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: Includes LanguageExt converters so <c>WorkflowDefinition</c>
/// (which uses <c>Arr&lt;T&gt;</c> and <c>HashMap&lt;string,V&gt;</c>) round-trips cleanly~ 💖
/// </remarks>
internal static class NatsJsonHelper
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Gets the shared JSON options with LanguageExt converters~ 🔧.</summary>
    public static JsonSerializerOptions JsonOptions => Options;

    /// <summary>Serialises an object to a JSON string~ 📝.</summary>
    public static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, Options);

    /// <summary>Deserialises a JSON string to the specified type~ 📖.</summary>
    public static T? Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, Options);

    // ── Private ──────────────────────────────────────────────────────────────

    private static JsonSerializerOptions CreateOptions()
    {
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        opts.Converters.Add(new ArrJsonConverterFactory());
        opts.Converters.Add(new HashMapStringJsonConverterFactory());
        return opts;
    }
}

// ── LanguageExt Converters ────────────────────────────────────────────────────

/// <summary>🏭 Factory for <c>Arr&lt;T&gt;</c> converters.</summary>
internal sealed class ArrJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type t) =>
        t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Arr<>);

    /// <inheritdoc/>
    public override JsonConverter CreateConverter(Type t, JsonSerializerOptions opts)
    {
        var elem = t.GetGenericArguments()[0];
        return (JsonConverter)Activator.CreateInstance(typeof(ArrJsonConverter<>).MakeGenericType(elem))!;
    }
}

/// <summary>📦 Converts <c>Arr&lt;T&gt;</c> to/from a JSON array~ ✨.</summary>
internal sealed class ArrJsonConverter<T> : JsonConverter<Arr<T>>
{
    /// <inheritdoc/>
    public override Arr<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return Arr<T>.Empty;
        }

        var list = JsonSerializer.Deserialize<List<T>>(ref reader, options);
        return list is null ? Arr<T>.Empty : new Arr<T>(list);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, Arr<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
        {
            JsonSerializer.Serialize(writer, item, options);
        }

        writer.WriteEndArray();
    }
}

/// <summary>🏭 Factory for <c>HashMap&lt;string, V&gt;</c> converters.</summary>
internal sealed class HashMapStringJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type t) =>
        t.IsGenericType
        && t.GetGenericTypeDefinition() == typeof(HashMap<,>)
        && t.GetGenericArguments()[0] == typeof(string);

    /// <inheritdoc/>
    public override JsonConverter CreateConverter(Type t, JsonSerializerOptions opts)
    {
        var valueType = t.GetGenericArguments()[1];
        return (JsonConverter)Activator.CreateInstance(typeof(HashMapJsonConverter<>).MakeGenericType(valueType))!;
    }
}

/// <summary>🗺️ Converts <c>HashMap&lt;string, V&gt;</c> to/from a JSON object~ ✨.</summary>
internal sealed class HashMapJsonConverter<V> : JsonConverter<HashMap<string, V>>
{
    /// <inheritdoc/>
    public override HashMap<string, V> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return HashMap<string, V>.Empty;
        }

        var dict = JsonSerializer.Deserialize<Dictionary<string, V>>(ref reader, options);
        if (dict is null)
        {
            return HashMap<string, V>.Empty;
        }

        return new HashMap<string, V>(dict.Select(kv => (kv.Key, kv.Value)));
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, HashMap<string, V> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (key, val) in value)
        {
            writer.WritePropertyName(key);
            JsonSerializer.Serialize(writer, val, options);
        }

        writer.WriteEndObject();
    }
}

