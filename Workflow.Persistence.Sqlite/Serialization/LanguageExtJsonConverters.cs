// <copyright file="LanguageExtJsonConverters.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Sqlite.Serialization;

using System.Text.Json;
using System.Text.Json.Serialization;
using LanguageExt;

/// <summary>
/// 🌸 System.Text.Json converters for LanguageExt immutable collection types~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: LanguageExt 4.x doesn't ship System.Text.Json converters out of the box.
/// <list type="bullet">
///   <item><see cref="ArrJsonConverter{T}"/> — serializes <c>Arr&lt;T&gt;</c> as a JSON array~ 📦</item>
///   <item><see cref="HashMapJsonConverter{V}"/> — serializes <c>HashMap&lt;string, V&gt;</c> as a JSON object~ 🗺️</item>
///   <item><see cref="NullableArrJsonConverter{T}"/> — handles nullable <c>Arr&lt;T&gt;?</c> (Tags on WorkflowDefinition)~ 🏷️</item>
/// </list>
/// Register all of them via <see cref="WorkflowJsonSerializerOptions.Create"/>~ 🔧
/// </remarks>
public static class LanguageExtJsonConverters
{
    /// <summary>
    /// Creates a <see cref="JsonSerializerOptions"/> instance pre-configured with all
    /// LanguageExt converters needed to round-trip <c>WorkflowDefinition</c>~ 🔧.
    /// </summary>
    public static JsonSerializerOptions CreateOptions()
    {
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        opts.Converters.Add(new ArrJsonConverterFactory());
        opts.Converters.Add(new HashMapStringJsonConverterFactory());
        return opts;
    }
}

/// <summary>
/// 🏭 Factory that creates <see cref="ArrJsonConverter{T}"/> for any <c>Arr&lt;T&gt;</c>~ ✨.
/// </summary>
public sealed class ArrJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        if (!typeToConvert.IsGenericType)
        {
            return false;
        }

        var def = typeToConvert.GetGenericTypeDefinition();
        return def == typeof(Arr<>);
    }

    /// <inheritdoc/>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var elementType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(ArrJsonConverter<>).MakeGenericType(elementType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

/// <summary>
/// 📦 Converts <c>Arr&lt;T&gt;</c> to/from a JSON array~ ✨.
/// </summary>
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

/// <summary>
/// 🏭 Factory that creates converters for <c>HashMap&lt;string, V&gt;</c>~ ✨.
/// </summary>
public sealed class HashMapStringJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        if (!typeToConvert.IsGenericType)
        {
            return false;
        }

        var def = typeToConvert.GetGenericTypeDefinition();
        if (def != typeof(HashMap<,>))
        {
            return false;
        }

        // Only handle HashMap<string, V> — key must be string for JSON object keys~ 🔑
        return typeToConvert.GetGenericArguments()[0] == typeof(string);
    }

    /// <inheritdoc/>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[1];
        var converterType = typeof(HashMapJsonConverter<>).MakeGenericType(valueType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

/// <summary>
/// 🗺️ Converts <c>HashMap&lt;string, V&gt;</c> to/from a JSON object~ ✨.
/// </summary>
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

