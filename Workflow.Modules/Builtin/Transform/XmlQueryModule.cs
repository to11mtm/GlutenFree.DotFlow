// <copyright file="XmlQueryModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Transform;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Transform.Internal;
using Workflow.Modules.Internal;

/// <summary>
/// 🏷️ Built-in XML Query module (<c>builtin.transform.xmlquery</c>) — evaluates an XPath expression
/// over XML data, XXE-safe (D10)~ ✨.
/// </summary>
public sealed class XmlQueryModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.transform.xmlquery";

    /// <inheritdoc />
    public string DisplayName => "XML Query (XPath)";

    /// <inheritdoc />
    public string Category => "Transformation";

    /// <inheritdoc />
    public string Description => "Evaluates an XPath expression over XML data (XXE-safe)~ 🏷️✨";

    /// <inheritdoc />
    public string Icon => "🏷️";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition("data", "Data", typeof(object), "XML string, or a dict from xml.read~ 📥", false)),
        Outputs: Arr.create(
            new PortDefinition("result", "Result", typeof(object), "Matched element(s) as dict/scalar~ 📤", false),
            new PortDefinition("matchCount", "Match Count", typeof(int), "Number of matches~ 🔢", false),
            new PortDefinition("success", "Success", typeof(bool), "Whether the query succeeded~ ✅", false)),
        Properties: Arr.create(
            new ModulePropertyDefinition("data", "Data", typeof(object), "XML data when not connected via port~ 📥", false, null, PropertyEditorType.MultilineText),
            new ModulePropertyDefinition("xpath", "XPath", typeof(string), "XPath expression~ 🏷️", true, null, PropertyEditorType.Text),
            new ModulePropertyDefinition("rootElement", "Root Element", typeof(string), "Root name when rebuilding XML from a dict~ 🌳", false, "root", PropertyEditorType.Text),
            new ModulePropertyDefinition("required", "Required", typeof(bool), "Fail when there are no matches~ ❗", false, false, PropertyEditorType.Boolean)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        if (TransformSupport.GetString(configuration, "xpath") is null)
        {
            return ValidationResult.Failure(new ValidationError("XPATH_REQUIRED", "xpath is required~ 💔", PropertyName: "xpath"));
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var xpath = TransformSupport.GetString(context.Properties, "xpath");
        if (xpath is null)
        {
            return Task.FromResult(ModuleResult.Fail("🏷️ xpath is required~ 💔"));
        }

        var required = TransformSupport.GetBool(context.Properties, "required");
        var rootElement = TransformSupport.GetString(context.Properties, "rootElement") ?? "root";
        var raw = TransformSupport.ReadData(context, "data");

        var sw = Stopwatch.StartNew();
        try
        {
            var doc = ToXDocument(raw, rootElement);
            if (doc?.Root is null)
            {
                return Task.FromResult(ModuleResult.Fail("🏷️ data is empty or not valid XML~ 💔"));
            }

            var matched = doc.XPathSelectElements(xpath).ToList();
            var values = matched.Select(XmlDictionaryConverter.ToDictionary).ToList();
            sw.Stop();

            if (values.Count == 0 && required)
            {
                return Task.FromResult(ModuleResult.Fail($"🏷️ XPath '{xpath}' matched nothing (required)~ 💔"));
            }

            object? result = values.Count == 1 ? values[0] : values;
            return Task.FromResult(ModuleResult.Ok(
                new Dictionary<string, object?>
                {
                    ["result"] = result,
                    ["matchCount"] = values.Count,
                    ["success"] = true,
                },
                ExecutionMetrics.FromDuration(sw.Elapsed)));
        }
        catch (XmlException ex)
        {
            return Task.FromResult(ModuleResult.Fail($"🏷️ Invalid XML: {ex.Message}~ 💔", ex));
        }
        catch (XPathException ex)
        {
            return Task.FromResult(ModuleResult.Fail($"🏷️ Invalid XPath '{xpath}': {ex.Message}~ 💔", ex));
        }
    }

    private static XDocument? ToXDocument(object? raw, string rootElement)
    {
        var normalized = TransformDataNormalizer.Normalize(raw);
        switch (normalized)
        {
            case null:
                return null;
            case string xml:
                // 🛡️ XXE-safe parse: DTD prohibited, no external resolver
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using (var stringReader = new StringReader(xml))
                using (var xmlReader = XmlReader.Create(stringReader, settings))
                {
                    return XDocument.Load(xmlReader);
                }

            case IReadOnlyDictionary<string, object?> dict:
                return new XDocument(XmlDictionaryConverter.FromDictionary(rootElement, dict));
            default:
                return null;
        }
    }
}
