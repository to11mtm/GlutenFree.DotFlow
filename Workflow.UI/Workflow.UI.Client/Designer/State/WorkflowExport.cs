// <copyright file="WorkflowExport.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Workflow.UI.Client.Api.Dtos;

/// <summary>
/// 📦 Phase 3.6 (E4) — the on-disk envelope for an exported workflow.
/// </summary>
/// <remarks>
/// The payload is the ordinary <see cref="WorkflowDto"/>, so export stays lossless by construction
/// (<c>ApiClientTests.Dtos_RoundTrip_NoDataLoss</c> already proves that shape round-trips). The
/// envelope adds only what a *file* needs and a wire response doesn't: a format version to refuse
/// on, and provenance for diagnostics.
/// <para>
/// A definition has no format version of its own — backward compatibility has so far relied on
/// adding trailing optional record parameters, which works inside a running system but is much
/// weaker once files exist on disk that outlive the deployment that wrote them.
/// </para>
/// </remarks>
/// <param name="DotflowFormat">The envelope format version. Unknown majors are refused.</param>
/// <param name="ExportedAt">When the file was produced (diagnostics only).</param>
/// <param name="EngineVersion">The engine version that produced it (diagnostics only).</param>
/// <param name="Workflow">The workflow itself.</param>
public sealed record WorkflowExportEnvelope(
    [property: JsonPropertyName("dotflowFormat")] int DotflowFormat,
    DateTimeOffset ExportedAt,
    string? EngineVersion,
    WorkflowDto Workflow);

/// <summary>The outcome of reading an export file~ 📥.</summary>
/// <param name="Workflow">The parsed workflow, or null when the file couldn't be read.</param>
/// <param name="Error">A human-readable failure reason, or null on success.</param>
/// <param name="Warnings">Non-fatal notes about the file itself.</param>
public sealed record WorkflowImportResult(
    WorkflowDto? Workflow,
    string? Error,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Whether the file was read successfully~ ✅.</summary>
    public bool Success => this.Workflow is not null && this.Error is null;

    /// <summary>Creates a failure result~ 💥.</summary>
    /// <param name="error">The reason.</param>
    /// <returns>The result.</returns>
    public static WorkflowImportResult Failed(string error) => new(null, error, Array.Empty<string>());
}

/// <summary>
/// 📥 What the import dialog hands back: the workflow to open, and whether saving it should
/// replace the existing workflow of the same id rather than create a copy (Q3)~ ✨.
/// </summary>
/// <param name="Workflow">The workflow to open in the designer.</param>
/// <param name="Overwrite">True to overwrite the existing workflow with this id.</param>
public sealed record WorkflowImportRequest(WorkflowDto Workflow, bool Overwrite);

/// <summary>
/// 📤 Phase 3.6 (E1/E2/E4) — writes and reads the workflow export format. Framework-free (D2) so
/// the same code can back a designer button, a CLI, or a future headless import endpoint~ ✨.
/// </summary>
public static class WorkflowExport
{
    /// <summary>The current envelope format version~ 🔢.</summary>
    public const int CurrentFormat = 1;

    /// <summary>The conventional file extension for an exported workflow~ 📄.</summary>
    public const string FileExtension = ".dotflow.json";

    /// <summary>
    /// Serialization options for the file format.
    /// </summary>
    /// <remarks>
    /// Three deliberate differences from the wire options:
    /// <list type="bullet">
    /// <item><description><b>Indented</b> — readability is the entire point of the format.</description></item>
    /// <item><description><b>Enums as names</b> — <c>"type": "String"</c> rather than <c>"type": 0</c>.
    /// Reads stay number-tolerant, so files written before this change (and anything hand-pasted
    /// from an API response) still load.</description></item>
    /// <item><description><b>Nulls omitted</b> — an exported file shouldn't be half empty keys.</description></item>
    /// </list>
    /// </remarks>
    public static JsonSerializerOptions Options { get; } = BuildOptions();

    /// <summary>Wraps a workflow in the current envelope and serializes it~ 📤.</summary>
    /// <param name="workflow">The workflow to export.</param>
    /// <param name="engineVersion">Optional engine version for provenance.</param>
    /// <returns>The file contents.</returns>
    public static string Write(WorkflowDto workflow, string? engineVersion = null)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        var envelope = new WorkflowExportEnvelope(
            CurrentFormat,
            DateTimeOffset.UtcNow,
            engineVersion,
            workflow);

        return JsonSerializer.Serialize(envelope, Options);
    }

    /// <summary>Reads an export file, tolerating a bare <see cref="WorkflowDto"/>~ 📥.</summary>
    /// <param name="json">The file contents.</param>
    /// <returns>The parsed workflow, or a legible failure.</returns>
    public static WorkflowImportResult Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return WorkflowImportResult.Failed("The file is empty.");
        }

        JsonElement root;
        try
        {
            root = JsonDocument.Parse(json).RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return WorkflowImportResult.Failed($"This isn't valid JSON — {ex.Message}");
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return WorkflowImportResult.Failed("A workflow file must be a JSON object.");
        }

        var warnings = new List<string>();

        // A bare WorkflowDto is accepted as "format 0" (Q6b) — people will paste API responses and
        // hand-craft files, and refusing them buys nothing.
        if (!TryGetProperty(root, "dotflowFormat", out var formatElement))
        {
            warnings.Add(
                "This file has no DotFlow envelope — reading it as a bare workflow. Re-export it to "
                + "get a versioned file.");
            return ReadWorkflow(root, warnings);
        }

        if (formatElement.ValueKind != JsonValueKind.Number || !formatElement.TryGetInt32(out var format))
        {
            return WorkflowImportResult.Failed("The 'dotflowFormat' field must be a number.");
        }

        if (format > CurrentFormat)
        {
            return WorkflowImportResult.Failed(
                $"This file is format v{format}, but this version of DotFlow only understands up to "
                + $"v{CurrentFormat}. Upgrade DotFlow to import it.");
        }

        if (format < 1)
        {
            return WorkflowImportResult.Failed($"Unknown workflow file format v{format}.");
        }

        if (!TryGetProperty(root, "workflow", out var workflowElement))
        {
            return WorkflowImportResult.Failed("The file has a DotFlow envelope but no 'workflow' inside it.");
        }

        return ReadWorkflow(workflowElement, warnings);
    }

    /// <summary>Builds a filesystem-safe filename for a workflow~ 📄.</summary>
    /// <param name="name">The workflow name.</param>
    /// <param name="version">The workflow version.</param>
    /// <returns>The suggested filename.</returns>
    public static string FileNameFor(string? name, string? version)
    {
        var slug = Slugify(name);
        var suffix = string.IsNullOrWhiteSpace(version) ? string.Empty : "-" + Slugify(version);
        return slug + suffix + FileExtension;
    }

    /// <summary>
    /// 🔒 E6 — node properties whose *names* look like credentials.
    /// </summary>
    /// <remarks>
    /// Secret <em>variables</em> are already safe: a secret declaration can't carry a value, by
    /// design, because definitions are exported and version-controlled. Node <b>properties</b> have
    /// no such guard — an HTTP node's <c>apiKey</c>, <c>bearerToken</c>, <c>password</c> and
    /// <c>oauth2ClientSecret</c> are ordinary strings and export verbatim.
    /// <para>
    /// This is name-based and therefore imperfect: it can't recognise a credential stored under an
    /// unusual name, and it will flag a property that merely looks credential-ish. It's a prompt to
    /// the user, not a guarantee — which is why redaction is opt-in and the warning says what it
    /// found rather than claiming the file is clean.
    /// </para>
    /// </remarks>
    private static readonly string[] CredentialNameFragments =
        ["password", "secret", "token", "apikey", "credential", "privatekey", "passphrase"];

    /// <summary>Finds credential-shaped properties carrying a non-empty literal value~ 🔒.</summary>
    /// <param name="workflow">The workflow about to be exported.</param>
    /// <returns>Findings as <c>nodeName → propertyName</c> pairs.</returns>
    public static IReadOnlyList<(string NodeName, string PropertyName)> FindCredentialProperties(WorkflowDto workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        var found = new List<(string, string)>();
        foreach (var node in workflow.Nodes ?? new List<NodeDto>())
        {
            foreach (var (name, value) in node.Properties ?? new Dictionary<string, JsonElement>())
            {
                if (value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var text = value.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                // A binding is not a credential — {{Variable.apiKey}} is exactly the safe pattern
                // we want people using, so flagging it would train them to ignore the warning.
                if (VariableTokens.ContainsToken(text))
                {
                    continue;
                }

                if (CredentialNameFragments.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)))
                {
                    found.Add((node.Name, name));
                }
            }
        }

        return found;
    }

    /// <summary>Returns a copy of the workflow with credential-shaped property values blanked~ 🔒.</summary>
    /// <param name="workflow">The workflow to redact.</param>
    /// <returns>A redacted copy; the original is untouched.</returns>
    public static WorkflowDto Redact(WorkflowDto workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        var flagged = FindCredentialProperties(workflow)
            .Select(f => (f.NodeName, f.PropertyName))
            .ToHashSet();

        if (flagged.Count == 0)
        {
            return workflow;
        }

        var nodes = (workflow.Nodes ?? new List<NodeDto>()).Select(node =>
        {
            var properties = new Dictionary<string, JsonElement>(node.Properties ?? new Dictionary<string, JsonElement>());
            foreach (var (name, _) in properties.ToList())
            {
                if (flagged.Contains((node.Name, name)))
                {
                    properties[name] = JsonSerializer.SerializeToElement(string.Empty);
                }
            }

            return node with { Properties = properties };
        }).ToList();

        return workflow with { Nodes = nodes };
    }

    private static WorkflowImportResult ReadWorkflow(JsonElement element, List<string> warnings)
    {
        WorkflowDto? workflow;
        try
        {
            workflow = element.Deserialize<WorkflowDto>(Options);
        }
        catch (JsonException ex)
        {
            return new WorkflowImportResult(null, $"This doesn't look like a DotFlow workflow — {ex.Message}", warnings);
        }

        if (workflow is null)
        {
            return new WorkflowImportResult(null, "This doesn't look like a DotFlow workflow.", warnings);
        }

        if (string.IsNullOrWhiteSpace(workflow.Name))
        {
            return new WorkflowImportResult(null, "The workflow has no name — it may not be a DotFlow file.", warnings);
        }

        // Guard the collections the designer assumes are non-null, so a hand-edited file with a
        // missing "nodes" key produces a clear message instead of a NullReferenceException later.
        workflow = workflow with
        {
            Nodes = workflow.Nodes ?? new List<NodeDto>(),
            Connections = workflow.Connections ?? new List<ConnectionDto>(),
        };

        return new WorkflowImportResult(workflow, null, warnings);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static string Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "workflow";
        }

        var chars = value.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();

        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        slug = slug.Trim('-');
        return slug.Length == 0 ? "workflow" : slug;
    }

    private static JsonSerializerOptions BuildOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        // Names on write, and numbers still accepted on read (the converter is tolerant both ways).
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
