// <copyright file="WorkflowJsonOptions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Postgres.Serialization;

using System.Text.Json;
using System.Text.Json.Serialization;
using LanguageExt;

/// <summary>
/// 🌸 System.Text.Json options with LanguageExt converters for <c>WorkflowDefinition</c> round-trips~ ✨💖
/// </summary>
public static class WorkflowJsonOptions
{
    /// <summary>
    /// Creates <see cref="JsonSerializerOptions"/> pre-configured with <c>Arr&lt;T&gt;</c> and
    /// <c>HashMap&lt;string, V&gt;</c> converters needed to round-trip <c>WorkflowDefinition</c>~ 🔧.
    /// </summary>
    public static JsonSerializerOptions Create()
    {
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        opts.Converters.Add(new ArrJsonConverterFactory());
        opts.Converters.Add(new HashMapStringJsonConverterFactory());
        return opts;
    }
}

/// <summary>🏭 Factory for <c>Arr&lt;T&gt;</c> converters~ ✨.</summary>
public sealed class ArrJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Arr<>);

    /// <inheritdoc/>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var elementType = typeToConvert.GetGenericArguments()[0];
        return (JsonConverter)Activator.CreateInstance(typeof(ArrJsonConverter<>).MakeGenericType(elementType))!;
    }
}

/// <summary>📦 Converts <c>Arr&lt;T&gt;</c> to/from a JSON array~ ✨.</summary>
/// <typeparam name="T">The element type.</typeparam>
public sealed class ArrJsonConverter<T> : JsonConverter<Arr<T>>
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

/// <summary>🏭 Factory for <c>HashMap&lt;string, V&gt;</c> converters~ ✨.</summary>
public sealed class HashMapStringJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        if (!typeToConvert.IsGenericType)
        {
            return false;
        }

        return typeToConvert.GetGenericTypeDefinition() == typeof(HashMap<,>)
            && typeToConvert.GetGenericArguments()[0] == typeof(string);
    }

    /// <inheritdoc/>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[1];
        return (JsonConverter)Activator.CreateInstance(typeof(HashMapJsonConverter<>).MakeGenericType(valueType))!;
    }
}

/// <summary>🗺️ Converts <c>HashMap&lt;string, V&gt;</c> to/from a JSON object~ ✨.</summary>
/// <typeparam name="V">The value type.</typeparam>
public sealed class HashMapJsonConverter<V> : JsonConverter<HashMap<string, V>>
{
    /// <inheritdoc/>
    public override HashMap<string, V> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return HashMap<string, V>.Empty;
        }

        var dict = JsonSerializer.Deserialize<Dictionary<string, V>>(ref reader, options);
        return dict is null
            ? HashMap<string, V>.Empty
            : new HashMap<string, V>(dict.Select(kv => (kv.Key, kv.Value)));
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

