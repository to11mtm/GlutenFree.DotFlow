// <copyright file="XmlWriteModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.File;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.File.Internal;

/// <summary>
/// 🏷️ Built-in XML Write module (<c>builtin.file.xml.write</c>) — writes a dictionary graph to
/// an XML file using the <c>@attr</c>/<c>#text</c>/auto-list convention~ 📁✨.
/// </summary>
public sealed class XmlWriteModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.file.xml.write";

    /// <inheritdoc />
    public string DisplayName => "Write XML";

    /// <inheritdoc />
    public string Category => "File System";

    /// <inheritdoc />
    public string Description => "Writes a dictionary graph to an XML file~ 🏷️✨";

    /// <inheritdoc />
    public string Icon => "🏷️";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition("data", "Data", typeof(object), "Dictionary graph to serialise~ 🏷️", false)),
        Outputs: Arr.create(
            new PortDefinition("bytesWritten", "Bytes Written", typeof(long), "Number of bytes written~ 📊", false),
            new PortDefinition("success", "Success", typeof(bool), "Whether the write succeeded~ ✅", false)),
        Properties: Arr.create(
            new ModulePropertyDefinition("path", "Path", typeof(string), "Output XML file path~ 📂", true, null, PropertyEditorType.FilePath),
            new ModulePropertyDefinition("data", "Data", typeof(object), "Dictionary graph when not connected via port~ 🏷️", false, null, PropertyEditorType.Json),
            new ModulePropertyDefinition("rootElement", "Root Element", typeof(string), "Root element name~ 🌳", false, "root", PropertyEditorType.Text),
            new ModulePropertyDefinition("indented", "Indented", typeof(bool), "Pretty-print with indentation~ 🎨", false, true, PropertyEditorType.Boolean),
            new ModulePropertyDefinition("encoding", "Encoding", typeof(string), "Text encoding~ 🔤", false, "utf-8", PropertyEditorType.Text)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        if (FileModuleSupport.GetString(configuration, "path") is null)
        {
            return ValidationResult.Failure(new ValidationError("PATH_REQUIRED", "path is required~ 💔", PropertyName: "path"));
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var rawPath = FileModuleSupport.GetString(context.Properties, "path");
        if (rawPath is null)
        {
            return ModuleResult.Fail("path is required~ 💔");
        }

        if (!FileModuleSupport.TryValidatePath(context, rawPath, PathAccessIntent.Write, out var path, out var failure))
        {
            return failure!;
        }

        var dataValue = context.Inputs.TryGetValue("data", out var portVal) && portVal is not null
            ? portVal
            : context.Properties.TryGetValue("data", out var propVal) ? propVal : null;

        if (!EncodingResolver.TryResolve(FileModuleSupport.GetString(context.Properties, "encoding"), out var encoding, out var encErr))
        {
            return ModuleResult.Fail($"🔤 {encErr}~ 💔");
        }

        var rootElement = FileModuleSupport.GetString(context.Properties, "rootElement") ?? "root";
        var indented = FileModuleSupport.GetBool(context.Properties, "indented", true);

        var sw = Stopwatch.StartNew();
        try
        {
            var root = XmlDictionaryConverter.FromDictionary(rootElement, dataValue);
            var doc = new XDocument(new XDeclaration("1.0", encoding.WebName, null), root);

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var settings = new XmlWriterSettings
            {
                Indent = indented,
                Encoding = encoding,
                Async = true,
            };

            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var xmlWriter = XmlWriter.Create(stream, settings))
            {
                doc.Save(xmlWriter);
            }

            sw.Stop();

            var outputs = new Dictionary<string, object?>
            {
                ["bytesWritten"] = new FileInfo(path).Length,
                ["success"] = true,
            };

            return ModuleResult.Ok(outputs, ExecutionMetrics.FromDuration(sw.Elapsed));
        }
        catch (XmlException ex)
        {
            return ModuleResult.Fail($"🏷️ Could not build XML: {ex.Message}~ 💔", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ModuleResult.Fail($"🏷️ Failed to write XML '{rawPath}': {ex.Message}~ 💔", ex);
        }
    }
}
