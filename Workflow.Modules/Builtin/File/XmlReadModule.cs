// <copyright file="XmlReadModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.File;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using System.Xml.XPath;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.File.Internal;

/// <summary>
/// 🏷️ Built-in XML Read module (<c>builtin.file.xml.read</c>) — parses an XML file into a
/// dictionary graph, with optional XSD validation and XPath pre-extraction~ 📁✨.
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.5.a.2. DTD processing is prohibited and the resolver is disabled to
/// prevent XXE attacks~ 🛡️.
/// </remarks>
public sealed class XmlReadModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.file.xml.read";

    /// <inheritdoc />
    public string DisplayName => "Read XML";

    /// <inheritdoc />
    public string Category => "File System";

    /// <inheritdoc />
    public string Description => "Parses an XML file into a dictionary graph~ 🏷️✨";

    /// <inheritdoc />
    public string Icon => "🏷️";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr<PortDefinition>.Empty,
        Outputs: Arr.create(
            new PortDefinition("data", "Data", typeof(object), "Parsed XML as a dictionary graph~ 🏷️", false),
            new PortDefinition("rootElement", "Root Element", typeof(string), "Name of the root element~ 🌳", false),
            new PortDefinition("success", "Success", typeof(bool), "Whether the parse succeeded~ ✅", false)),
        Properties: Arr.create(
            new ModulePropertyDefinition("path", "Path", typeof(string), "XML file path. Supports {{Variable.Name}}~ 📂", true, null, PropertyEditorType.FilePath),
            new ModulePropertyDefinition("encoding", "Encoding", typeof(string), "Text encoding~ 🔤", false, "utf-8", PropertyEditorType.Text),
            new ModulePropertyDefinition("validateSchema", "Validate Schema", typeof(bool), "Validate against an XSD~ 🛡️", false, false, PropertyEditorType.Boolean),
            new ModulePropertyDefinition("schemaPath", "Schema Path", typeof(string), "XSD file path (when validateSchema)~ 📐", false, null, PropertyEditorType.FilePath),
            new ModulePropertyDefinition("xpath", "XPath", typeof(string), "Optional XPath to pre-extract before conversion~ 🎯", false, null, PropertyEditorType.Text)));

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

        if (!FileModuleSupport.TryValidatePath(context, rawPath, PathAccessIntent.Read, out var path, out var failure))
        {
            return failure!;
        }

        if (!System.IO.File.Exists(path))
        {
            return ModuleResult.Fail($"🏷️ File not found: '{rawPath}'~ 💔");
        }

        if (!EncodingResolver.TryResolve(FileModuleSupport.GetString(context.Properties, "encoding"), out var encoding, out var encErr))
        {
            return ModuleResult.Fail($"🔤 {encErr}~ 💔");
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var text = await System.IO.File.ReadAllTextAsync(path, encoding, cancellationToken).ConfigureAwait(false);

            // 🛡️ XXE-safe: no DTD, no external resolver
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            };

            XDocument doc;
            using (var stringReader = new StringReader(text))
            using (var xmlReader = XmlReader.Create(stringReader, settings))
            {
                doc = XDocument.Load(xmlReader);
            }

            if (doc.Root is null)
            {
                return ModuleResult.Fail($"🏷️ XML '{rawPath}' has no root element~ 💔");
            }

            // Optional XSD validation~ 🛡️
            if (FileModuleSupport.GetBool(context.Properties, "validateSchema", false))
            {
                var schemaRaw = FileModuleSupport.GetString(context.Properties, "schemaPath");
                if (schemaRaw is null)
                {
                    return ModuleResult.Fail("🏷️ validateSchema is true but schemaPath is not set~ 💔");
                }

                if (!FileModuleSupport.TryValidatePath(context, schemaRaw, PathAccessIntent.Read, out var schemaPath, out var schemaFail))
                {
                    return schemaFail!;
                }

                var violations = ValidateAgainstSchema(doc, schemaPath);
                if (violations.Count > 0)
                {
                    return ModuleResult.Fail($"🏷️ Schema validation failed: {string.Join("; ", violations)}~ 💔");
                }
            }

            XElement target = doc.Root;
            var xpath = FileModuleSupport.GetString(context.Properties, "xpath");
            if (xpath is not null)
            {
                var extracted = doc.XPathSelectElement(xpath);
                if (extracted is null)
                {
                    return ModuleResult.Fail($"🏷️ XPath '{xpath}' matched no element~ 💔");
                }

                target = extracted;
            }

            var data = XmlDictionaryConverter.ToDictionary(target);
            sw.Stop();

            var outputs = new Dictionary<string, object?>
            {
                ["data"] = data,
                ["rootElement"] = target.Name.LocalName,
                ["success"] = true,
            };

            return ModuleResult.Ok(outputs, ExecutionMetrics.FromDuration(sw.Elapsed));
        }
        catch (XmlException ex)
        {
            return ModuleResult.Fail($"🏷️ Invalid XML in '{rawPath}': {ex.Message}~ 💔", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ModuleResult.Fail($"🏷️ Failed to read XML '{rawPath}': {ex.Message}~ 💔", ex);
        }
    }

    private static List<string> ValidateAgainstSchema(XDocument doc, string schemaPath)
    {
        var violations = new List<string>();
        var schemas = new XmlSchemaSet();

        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using (var reader = XmlReader.Create(schemaPath, settings))
        {
            schemas.Add(null, reader);
        }

        doc.Validate(schemas, (_, e) => violations.Add(e.Message));
        return violations;
    }
}
