// <copyright file="StringTransformModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Transform;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Transform.Internal;

/// <summary>
/// 📝 Built-in String Transform module (<c>builtin.transform.string</c>) — the string utility belt
/// (case/trim/substring/replace/split/join/pad/truncate/format/regex/encodings/hash/guid, D11)~ ✨.
/// </summary>
public sealed class StringTransformModule : IWorkflowModule
{
    private static readonly System.Collections.Generic.HashSet<string> KnownOps = new(StringComparer.OrdinalIgnoreCase)
    {
        "upper", "lower", "trim", "trimstart", "trimend", "substring", "replace", "split", "join",
        "padleft", "padright", "truncate", "format", "regexmatch", "regexreplace", "regexextract",
        "base64encode", "base64decode", "urlencode", "urldecode", "htmlencode", "htmldecode", "hash", "newguid",
    };

    /// <inheritdoc />
    public string ModuleId => "builtin.transform.string";

    /// <inheritdoc />
    public string DisplayName => "String Operations";

    /// <inheritdoc />
    public string Category => "Transformation";

    /// <inheritdoc />
    public string Description => "Case, trim, substring, replace, split/join, regex, encode, hash, guid~ 📝✨";

    /// <inheritdoc />
    public string Icon => "📝";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition("input", "Input", typeof(object), "String or array of strings~ 📥", false)),
        Outputs: Arr.create(
            new PortDefinition("result", "Result", typeof(object), "Transformed string(s)~ 📤", false),
            new PortDefinition("success", "Success", typeof(bool), "Whether the operation succeeded~ ✅", false)),
        Properties: Arr.create(
            new ModulePropertyDefinition("input", "Input", typeof(object), "Input when not connected via port~ 📥", false, null, PropertyEditorType.MultilineText),
            new ModulePropertyDefinition("operation", "Operation", typeof(string), "The string operation~ 📝", true, "upper", PropertyEditorType.Dropdown),
            new ModulePropertyDefinition("parameters", "Parameters", typeof(object), "Operation-specific parameters~ ⚙️", false, null, PropertyEditorType.Json)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        var op = TransformSupport.GetString(configuration, "operation");
        if (op is null || !KnownOps.Contains(op))
        {
            return ValidationResult.Failure(new ValidationError("INVALID_OPERATION", $"unknown string operation '{op}'~ 💔", PropertyName: "operation"));
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var operation = (TransformSupport.GetString(context.Properties, "operation") ?? string.Empty).ToLowerInvariant();
        if (!KnownOps.Contains(operation))
        {
            return Task.FromResult(ModuleResult.Fail($"📝 Unknown operation '{operation}'~ 💔"));
        }

        var parameters = TransformDataNormalizer.Normalize(context.Properties.GetValueOrDefault("parameters")) as IReadOnlyDictionary<string, object?>
            ?? new Dictionary<string, object?>();

        var rawInput = TransformSupport.ReadData(context, "input");
        var normalized = TransformDataNormalizer.Normalize(rawInput);

        var sw = Stopwatch.StartNew();
        try
        {
            object? result;

            // 'newguid' needs no input; 'join' consumes an array → single string~
            if (operation == "newguid")
            {
                result = Guid.NewGuid().ToString();
            }
            else if (operation == "join")
            {
                var sep = GetParam(parameters, "separator") ?? ",";
                var items = normalized as IReadOnlyList<object?> ?? new List<object?> { normalized };
                result = string.Join(sep, items.Select(i => i?.ToString() ?? string.Empty));
            }
            else if (normalized is IReadOnlyList<object?> list)
            {
                var outList = new List<object?>();
                foreach (var item in list)
                {
                    outList.Add(ApplyOp(operation, item?.ToString(), parameters));
                }

                result = outList;
            }
            else
            {
                result = ApplyOp(operation, normalized?.ToString(), parameters);
            }

            sw.Stop();
            return Task.FromResult(ModuleResult.Ok(
                new Dictionary<string, object?> { ["result"] = result, ["success"] = true },
                ExecutionMetrics.FromDuration(sw.Elapsed)));
        }
        catch (TransformModuleException ex)
        {
            return Task.FromResult(ModuleResult.Fail($"📝 {ex.Message}~ 💔", ex));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return Task.FromResult(ModuleResult.Fail($"📝 String {operation} failed: {ex.Message}~ 💔", ex));
        }
    }

    private static object? ApplyOp(string operation, string? input, IReadOnlyDictionary<string, object?> p)
    {
        var s = input ?? string.Empty;

        switch (operation)
        {
            case "upper": return s.ToUpperInvariant();
            case "lower": return s.ToLowerInvariant();
            case "trim": return s.Trim();
            case "trimstart": return s.TrimStart();
            case "trimend": return s.TrimEnd();
            case "substring":
                var start = GetInt(p, "start") ?? 0;
                var len = GetInt(p, "length");
                start = Math.Clamp(start, 0, s.Length);
                var available = s.Length - start;
                var take = len is { } l ? Math.Clamp(l, 0, available) : available;
                return s.Substring(start, take);
            case "replace":
                return s.Replace(GetParam(p, "find") ?? string.Empty, GetParam(p, "with") ?? string.Empty);
            case "split":
                return s.Split(GetParam(p, "separator") ?? ",").Cast<object?>().ToList();
            case "padleft":
                return s.PadLeft(GetInt(p, "width") ?? s.Length, GetChar(p, "char"));
            case "padright":
                return s.PadRight(GetInt(p, "width") ?? s.Length, GetChar(p, "char"));
            case "truncate":
                var max = GetInt(p, "length") ?? s.Length;
                var ellipsis = GetParam(p, "ellipsis") ?? "…";
                return s.Length <= max ? s : s[..Math.Max(0, max - ellipsis.Length)] + ellipsis;
            case "format":
                return (GetParam(p, "template") ?? "{0}").Replace("{0}", s).Replace("{item}", s);
            case "regexmatch":
                return SafeRegex.Create(GetParam(p, "pattern") ?? string.Empty).IsMatch(s);
            case "regexreplace":
                return SafeRegex.Create(GetParam(p, "pattern") ?? string.Empty).Replace(s, GetParam(p, "with") ?? string.Empty);
            case "regexextract":
                var m = SafeRegex.Create(GetParam(p, "pattern") ?? string.Empty).Match(s);
                var group = GetInt(p, "group") ?? 0;
                return m.Success && group < m.Groups.Count ? m.Groups[group].Value : null;
            case "base64encode":
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
            case "base64decode":
                return Encoding.UTF8.GetString(Convert.FromBase64String(s));
            case "urlencode":
                return Uri.EscapeDataString(s);
            case "urldecode":
                return Uri.UnescapeDataString(s);
            case "htmlencode":
                return System.Net.WebUtility.HtmlEncode(s);
            case "htmldecode":
                return System.Net.WebUtility.HtmlDecode(s);
            case "hash":
                return Hash(s, GetParam(p, "algorithm") ?? "sha256");
            default:
                throw new TransformModuleException($"unhandled operation '{operation}'");
        }
    }

    private static string Hash(string input, string algorithm)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        byte[] hash = algorithm.ToLowerInvariant() switch
        {
            "sha256" => SHA256.HashData(bytes),
            "sha512" => SHA512.HashData(bytes),
            "md5" => MD5.HashData(bytes), // ⚠️ legacy interop only — non-cryptographic
            _ => throw new TransformModuleException($"unknown hash algorithm '{algorithm}' (use sha256/sha512/md5)"),
        };

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? GetParam(IReadOnlyDictionary<string, object?> p, string key)
        => p.TryGetValue(key, out var v) && v is not null ? v.ToString() : null;

    private static int? GetInt(IReadOnlyDictionary<string, object?> p, string key)
    {
        if (!p.TryGetValue(key, out var v) || v is null)
        {
            return null;
        }

        return v switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) => r,
            _ => null,
        };
    }

    private static char GetChar(IReadOnlyDictionary<string, object?> p, string key)
    {
        var s = GetParam(p, key);
        return string.IsNullOrEmpty(s) ? ' ' : s[0];
    }
}
