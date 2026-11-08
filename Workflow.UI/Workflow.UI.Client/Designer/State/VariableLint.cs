// <copyright file="VariableLint.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// 🧭 Phase 3.5 (V7) — catches broken <c>{{…}}</c> bindings at design time.
/// </summary>
/// <remarks>
/// <para>
/// This matters much more since V4: an unresolvable reference in a template-enabled property now
/// <b>fails the node</b> rather than passing through as literal text, so a typo that used to
/// produce a puzzling downstream value now stops the run. Catching it in the designer turns a
/// runtime failure into a red squiggle.
/// </para>
/// <para>
/// The parsing rules deliberately mirror <c>PropertyBinder</c>: the same escape
/// (<c>\{\{</c>), the same "pure reference vs. expression" split, and the same tolerance for
/// dotted identifiers inside expressions (<c>Math.max</c> is not a node reference).
/// </para>
/// </remarks>
public static class VariableLint
{
    /// <summary>The builtin module that writes a variable at run time~ ✍️.</summary>
    public const string SetVariableModuleId = "builtin.setvariable";

    /// <summary>The reserved root meaning "this node's own incoming value"~ 🔌.</summary>
    public const string SelfInputRoot = "input";

    /// <summary>The reserved root meaning "the item currently flowing through a region"~ 🌊.</summary>
    public const string StreamItemRoot = "item";

    /// <summary>
    /// §10.2 — node metadata key recording "keep the variable" choices for name clashes, as
    /// comma-separated <c>name@sourceNodeId.sourcePort</c> entries (so a rewire raises it again).
    /// </summary>
    public const string ClashAckKey = "variableClashAck";

    private const string VariablePrefix = "Variable.";

    /// <summary>Matches an unescaped <c>{{ … }}</c> token, exactly as the binder does.</summary>
    private static readonly Regex TokenPattern = new(@"(?<!\\)\{\{\s*(.+?)\s*\}\}", RegexOptions.Compiled);

    /// <summary>A dotted identifier path with no operators, literals or calls.</summary>
    private static readonly Regex PureReferencePattern = new(
        @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z0-9_]+)+$",
        RegexOptions.Compiled);

    /// <summary>Finds <c>Variable.name</c> occurrences inside an expression.</summary>
    private static readonly Regex VariableTokenPattern = new(
        @"(?<![A-Za-z0-9_.])Variable\.([A-Za-z0-9_]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Lints every template-enabled property in the document~ 🧭.</summary>
    /// <param name="doc">The document.</param>
    /// <param name="globals">Known global variable names, when the store is reachable.</param>
    /// <returns>The binding issues found.</returns>
    public static IReadOnlyList<GraphIssue> Validate(
        DesignerDocument doc,
        IReadOnlyCollection<string>? globals = null)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var issues = new List<GraphIssue>();
        var writers = SetVariableWriters(doc);

        foreach (var node in doc.Nodes)
        {
            var clashes = TemplatedValues(node)
                .SelectMany(p => Clashes(doc, node, p.Name, p.Value, globals))
                .ToList();
            foreach (var clash in clashes)
            {
                issues.Add(new GraphIssue(
                    clash.VariableExists ? IssueSeverity.Warning : IssueSeverity.Error,
                    $"'{node.Name}' → {clash.PropertyName}: {clash.Message}",
                    node.Id));
            }

            foreach (var reference in ReferencesIn(node))
            {
                // Already reported, with a better message, as a clash~
                if (reference.Kind == ReferenceKind.Variable
                    && clashes.Any(c => !c.VariableExists
                        && c.PropertyName == reference.PropertyName
                        && string.Equals(c.Name, reference.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                issues.AddRange(Judge(doc, node, reference, globals, writers));
            }
        }

        issues.AddRange(UnassignedDeclarations(doc, globals, writers));
        return issues;
    }

    /// <summary>
    /// Returns the references in one property that the run would fail on, for the editor's badge~ ⚠️.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="node">The node being edited.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="value">The property value text.</param>
    /// <param name="globals">Known global variable names.</param>
    /// <returns>True when at least one reference can't resolve.</returns>
    public static bool HasUnresolvableReference(
        DesignerDocument doc,
        DesignerNode node,
        string propertyName,
        string? value,
        IReadOnlyCollection<string>? globals = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(node);

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var writers = SetVariableWriters(doc);
        return ParseReferences(propertyName, value)
            .SelectMany(r => Judge(doc, node, r, globals, writers))
            .Any(i => i.Severity == IssueSeverity.Error);
    }

    /// <summary>Escapes every unescaped <c>{{</c> so it survives binding as literal text~ 🚪.</summary>
    /// <param name="value">The property value.</param>
    /// <returns>The escaped text.</returns>
    public static string EscapeBraces(string? value)
        => string.IsNullOrEmpty(value) ? value ?? string.Empty : TokenPattern.Replace(value, m => @"\{\{" + m.Value[2..]);

    /// <summary>
    /// Whether <em>every</em> reference in the value is unresolvable, so escaping the whole value
    /// can't break a working binding. Gates the migration quick-fix (V7.4)~ 🧹.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="node">The node being edited.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="value">The property value text.</param>
    /// <param name="globals">Known global variable names.</param>
    /// <returns>True when the value contains references and none of them resolve.</returns>
    public static bool AllReferencesUnresolvable(
        DesignerDocument doc,
        DesignerNode node,
        string propertyName,
        string? value,
        IReadOnlyCollection<string>? globals = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(node);

        var references = ParseReferences(propertyName, value).ToList();
        if (references.Count == 0)
        {
            return false;
        }

        var writers = SetVariableWriters(doc);
        return references.All(r => Judge(doc, node, r, globals, writers).Any(i => i.Severity == IssueSeverity.Error));
    }

    /// <summary>
    /// Lists the distinct <c>{{Variable.x}}</c> names referenced in a value~ 🔗.
    /// </summary>
    /// <remarks>
    /// Exposed for the import report (E5), which needs to know which variables a workflow expects
    /// its environment to supply. Uses the same parsing as the lint, so the two can't disagree.
    /// </remarks>
    /// <param name="value">The property value text.</param>
    /// <returns>The referenced variable names.</returns>
    public static IEnumerable<string> ReferencedVariableNames(string? value)
        => ParseReferences(string.Empty, value)
            .Where(r => r.Kind == ReferenceKind.Variable)
            .Select(r => r.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// §10.2 — a <c>{{Variable.x}}</c> in a node whose input port <c>x</c> is wired: the token reads
    /// the variable, never the value on the port. Easy to mix up, so the designer asks~ 🔀.
    /// </summary>
    /// <param name="PropertyName">The property holding the token.</param>
    /// <param name="Name">The clashing name (as wired: the port's spelling).</param>
    /// <param name="SourceNodeId">The node wired into that port.</param>
    /// <param name="SourcePortName">Its output port.</param>
    /// <param name="VariableExists">Whether a variable of that name exists (declared, global, set, body value).</param>
    /// <param name="IsDeclared">Whether it's declared on this workflow (so it can be renamed here).</param>
    public sealed record VariableClash(
        string PropertyName,
        string Name,
        string SourceNodeId,
        string SourcePortName,
        bool VariableExists,
        bool IsDeclared)
    {
        /// <summary>Gets the token that reads the incoming value instead.</summary>
        public string IncomingToken => VariableTokens.OutputToken(this.SourceNodeId, this.SourcePortName);

        /// <summary>Gets the acknowledgement entry stored in <see cref="ClashAckKey"/>.</summary>
        public string AckEntry => $"{this.Name}@{this.SourceNodeId}.{this.SourcePortName}";

        /// <summary>Gets the explanation shown in the designer.</summary>
        public string Message => this.VariableExists
            ? $"{{{{Variable.{this.Name}}}}} reads the variable '{this.Name}', not the value wired into this "
              + $"node's '{this.Name}' port ({this.IncomingToken}). Pick which one you mean."
            : $"{{{{Variable.{this.Name}}}}} reads a variable, and there's no variable '{this.Name}' — the "
              + $"value wired into the '{this.Name}' port is {this.IncomingToken}. This node will fail at run time.";
    }

    /// <summary>
    /// §10.2 — the unacknowledged name clashes in one property value: a <c>{{Variable.x}}</c> where
    /// <c>x</c> is also one of the node's wired input ports~ 🔀.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="node">The node.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="value">The property value text.</param>
    /// <param name="globals">Known global variable names.</param>
    /// <returns>One clash per clashing name.</returns>
    public static IReadOnlyList<VariableClash> Clashes(
        DesignerDocument doc,
        DesignerNode node,
        string propertyName,
        string? value,
        IReadOnlyCollection<string>? globals = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(node);

        var names = ParseReferences(propertyName, value)
            .Where(r => r.Kind == ReferenceKind.Variable)
            .Select(r => r.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0)
        {
            return [];
        }

        var acknowledged = AcknowledgedEntries(node);
        Dictionary<string, List<string>>? writers = null;
        var clashes = new List<VariableClash>();
        foreach (var name in names)
        {
            var wire = doc.Connections.FirstOrDefault(c =>
                c.TargetNodeId == node.Id && string.Equals(c.TargetPortName, name, StringComparison.OrdinalIgnoreCase));
            if (wire is null)
            {
                continue;
            }

            writers ??= SetVariableWriters(doc);
            var declared = doc.Variables.Keys.Contains(name, StringComparer.OrdinalIgnoreCase);
            var exists = declared
                || (globals?.Contains(name, StringComparer.OrdinalIgnoreCase) ?? false)
                || writers.ContainsKey(name)
                || VariableTokens.BodyValuesFor(doc, node.Id).Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
            var clash = new VariableClash(propertyName, wire.TargetPortName, wire.SourceNodeId, wire.SourcePortName, exists, declared);
            if (!acknowledged.Contains(clash.AckEntry))
            {
                clashes.Add(clash);
            }
        }

        return clashes;
    }

    /// <summary>"Use the incoming value": rewrites <c>Variable.x</c> to <c>sourceNode.port</c> inside tokens~ 🔀.</summary>
    /// <param name="value">The property value text.</param>
    /// <param name="clash">The clash being resolved.</param>
    /// <returns>The rewritten text (paths below the root are kept: <c>Variable.x.a</c> → <c>src.port.a</c>).</returns>
    public static string UseIncomingValue(string value, VariableClash clash)
    {
        ArgumentNullException.ThrowIfNull(clash);
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var reference = new Regex(
            $@"(?<![A-Za-z0-9_.])Variable\.{Regex.Escape(clash.Name)}(?![A-Za-z0-9_])",
            RegexOptions.IgnoreCase);
        return TokenPattern.Replace(
            value,
            token => reference.Replace(token.Value, $"{clash.SourceNodeId}.{clash.SourcePortName}"));
    }

    /// <summary>"Keep the variable": the node's <see cref="ClashAckKey"/> value with this clash added~ ✅.</summary>
    /// <param name="node">The node.</param>
    /// <param name="clash">The clash being acknowledged.</param>
    /// <returns>The new metadata value.</returns>
    public static string AcknowledgedWith(DesignerNode node, VariableClash clash)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(clash);

        var entries = AcknowledgedEntries(node).ToList();
        if (!entries.Contains(clash.AckEntry, StringComparer.OrdinalIgnoreCase))
        {
            entries.Add(clash.AckEntry);
        }

        return string.Join(",", entries);
    }

    private static HashSet<string> AcknowledgedEntries(DesignerNode node)
        => node.Metadata.TryGetValue(ClashAckKey, out var raw) && !string.IsNullOrWhiteSpace(raw)
            ? raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<(string Name, string? Value)> TemplatedValues(DesignerNode node)
    {
        var templated = node.Schema?.Properties?
            .Where(p => p.SupportsTemplates)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (templated is null || templated.Count == 0)
        {
            yield break;
        }

        foreach (var (name, value) in node.Properties)
        {
            if (value.ValueKind == JsonValueKind.String && templated.Contains(name))
            {
                yield return (name, value.GetString());
            }
        }
    }

    /// <summary>A single <c>{{…}}</c> reference found in a property~ 🔗.</summary>
    /// <param name="PropertyName">The property it was found in.</param>
    /// <param name="Token">The literal token text.</param>
    /// <param name="Kind">Whether it names a variable or a node output.</param>
    /// <param name="Name">The variable name, or the node id.</param>
    private sealed record Reference(string PropertyName, string Token, ReferenceKind Kind, string Name);

    private enum ReferenceKind
    {
        Variable,
        NodeOutput,
    }

    private static IEnumerable<GraphIssue> Judge(
        DesignerDocument doc,
        DesignerNode node,
        Reference reference,
        IReadOnlyCollection<string>? globals,
        IReadOnlyDictionary<string, List<string>> writers)
    {
        if (reference.Kind == ReferenceKind.NodeOutput)
        {
            // 🔌 'input' is a reserved root meaning "this node's own incoming value" — it never
            // names a node, so it must not be looked up as one (docs/variables.md).
            if (string.Equals(reference.Name, SelfInputRoot, StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            // 🌊 'item' is the per-item root inside a streaming region — meaningless outside one,
            // and saying so is far kinder than "no node called 'item'" (5.1.2, Q5).
            if (string.Equals(reference.Name, StreamItemRoot, StringComparison.OrdinalIgnoreCase))
            {
                if (!StreamGraph.RegionIndexByNode(doc).ContainsKey(node.Id))
                {
                    yield return new GraphIssue(
                        IssueSeverity.Error,
                        $"'{node.Name}' → {reference.PropertyName}: {reference.Token} refers to the current "
                        + "streaming item, but this node isn't inside a streaming region. Use {{input}} for "
                        + "the value coming in, or wire this node's streaming ports.",
                        node.Id);
                }

                yield break;
            }

            if (doc.FindNode(reference.Name) is null)
            {
                yield return new GraphIssue(
                    IssueSeverity.Error,
                    $"'{node.Name}' → {reference.PropertyName}: {reference.Token} refers to node "
                    + $"'{reference.Name}', which isn't in this workflow. If you meant a literal "
                    + @"'{{', escape it as '\{\{'.",
                    node.Id);
            }
            else if (reference.Name != node.Id && !VariableTokens.UpstreamOf(doc, node.Id).Contains(reference.Name))
            {
                // 🧬 §10.1 — only ancestors (any connection path in, across bodies) are bindable~
                yield return new GraphIssue(
                    IssueSeverity.Error,
                    $"'{node.Name}' → {reference.PropertyName}: {reference.Token} reads "
                    + $"'{doc.FindNode(reference.Name)!.Name}', which isn't upstream of this node, so this node will "
                    + "fail at run time. Wire it in, or pass the value through a variable.",
                    node.Id);
            }

            yield break;
        }

        if (doc.Variables.Keys.Contains(reference.Name, StringComparer.OrdinalIgnoreCase))
        {
            yield break;
        }

        // 🔁 §10.2 — item / index / error, handed to this node by the loop / branch / catch around it~
        if (VariableTokens.BodyValuesFor(doc, node.Id)
            .Any(v => string.Equals(v.Name, reference.Name, StringComparison.OrdinalIgnoreCase)))
        {
            yield break;
        }

        if (globals is not null && globals.Contains(reference.Name, StringComparer.OrdinalIgnoreCase))
        {
            yield break;
        }

        if (writers.TryGetValue(reference.Name, out var writerIds))
        {
            // Written somewhere. If a writer is upstream the value is guaranteed to exist by the
            // time this node runs; otherwise it's a genuine maybe, so warn rather than block.
            var upstream = VariableTokens.UpstreamOf(doc, node.Id);
            if (writerIds.Any(upstream.Contains))
            {
                yield break;
            }

            var names = string.Join(", ", writerIds.Select(id => doc.FindNode(id)?.Name ?? id));
            yield return new GraphIssue(
                IssueSeverity.Warning,
                $"'{node.Name}' → {reference.PropertyName}: {reference.Token} is set by {names}, "
                + "which isn't upstream of this node — it may not have run yet.",
                node.Id);
            yield break;
        }

        yield return new GraphIssue(
            IssueSeverity.Error,
            $"'{node.Name}' → {reference.PropertyName}: no variable named '{reference.Name}' is "
            + "declared and nothing sets it, so this node will fail at run time.",
            node.Id);
    }

    private static IEnumerable<GraphIssue> UnassignedDeclarations(
        DesignerDocument doc,
        IReadOnlyCollection<string>? globals,
        IReadOnlyDictionary<string, List<string>> writers)
    {
        foreach (var declared in WorkflowVariables.List(doc))
        {
            if (declared.InitialValue is not null
                || declared.IsSecret
                || writers.ContainsKey(declared.Name)
                || (globals?.Contains(declared.Name, StringComparer.OrdinalIgnoreCase) ?? false))
            {
                continue;
            }

            // Declaring a variable is a contract (Q15), so this never fails the run — it starts as
            // null. Still worth saying out loud, because the usual cause is a forgotten run input.
            yield return new GraphIssue(
                IssueSeverity.Warning,
                $"Variable '{declared.Name}' has no initial value and nothing sets it — "
                + "it starts as null unless supplied as a run input.");
        }
    }

    /// <summary>Maps variable name → ids of the SetVariable nodes writing it.</summary>
    private static Dictionary<string, List<string>> SetVariableWriters(DesignerDocument doc)
    {
        var writers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in doc.Nodes.Where(n =>
                     string.Equals(n.ModuleId, SetVariableModuleId, StringComparison.OrdinalIgnoreCase)))
        {
            if (!node.Properties.TryGetValue("name", out var element)
                || element.ValueKind != JsonValueKind.String
                || element.GetString() is not { Length: > 0 } name)
            {
                continue;
            }

            if (!writers.TryGetValue(name, out var ids))
            {
                ids = new List<string>();
                writers[name] = ids;
            }

            ids.Add(node.Id);
        }

        return writers;
    }

    private static IEnumerable<Reference> ReferencesIn(DesignerNode node)
    {
        // Only properties the schema marks template-enabled are expanded at run time (V4), so
        // anything else can legitimately contain "{{" and must not be linted. Without a schema we
        // can't tell — stay quiet rather than invent errors.
        var templated = node.Schema?.Properties?
            .Where(p => p.SupportsTemplates)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (templated is null || templated.Count == 0)
        {
            yield break;
        }

        foreach (var (name, value) in node.Properties)
        {
            if (value.ValueKind != JsonValueKind.String || !templated.Contains(name))
            {
                continue;
            }

            foreach (var reference in ParseReferences(name, value.GetString()))
            {
                yield return reference;
            }
        }
    }

    private static IEnumerable<Reference> ParseReferences(string propertyName, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            yield break;
        }

        foreach (Match match in TokenPattern.Matches(value))
        {
            var inner = match.Groups[1].Value.Trim();

            if (PureReferencePattern.IsMatch(inner))
            {
                if (inner.StartsWith(VariablePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    yield return new Reference(
                        propertyName, match.Value, ReferenceKind.Variable, RootOf(inner[VariablePrefix.Length..]));
                }
                else
                {
                    yield return new Reference(
                        propertyName, match.Value, ReferenceKind.NodeOutput, RootOf(inner));
                }

                continue;
            }

            // An expression: the binder resolves Variable./nodeId. tokens inside it and leaves
            // everything else (Math.max, JSON.parse) for the evaluator — so only judge the
            // Variable. ones here.
            foreach (Match variable in VariableTokenPattern.Matches(inner))
            {
                yield return new Reference(
                    propertyName, match.Value, ReferenceKind.Variable, variable.Groups[1].Value);
            }
        }
    }

    private static string RootOf(string dottedPath)
    {
        var dot = dottedPath.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 ? dottedPath[..dot] : dottedPath;
    }
}
