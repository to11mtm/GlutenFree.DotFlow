// <copyright file="WireJson.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Api;

using System.Text.Json;
using Workflow.Persistence.Sqlite.Serialization;

/// <summary>
/// 🔤 Phase 3.3.a.0 — Produces the *exact* JSON the API emits, by using the same converters +
/// camelCase (Web) defaults the server registers (<c>Program.cs</c>). Used to prove the client
/// DTO mirrors round-trip real wire data losslessly (D2/D5)~ ✨.
/// </summary>
public static class WireJson
{
    /// <summary>The server-equivalent JSON options (camelCase + LanguageExt converters)~ 🔤.</summary>
    public static readonly JsonSerializerOptions ServerOptions = BuildServerOptions();

    private static JsonSerializerOptions BuildServerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new ArrJsonConverterFactory());
        options.Converters.Add(new HashMapStringJsonConverterFactory());
        return options;
    }
}
