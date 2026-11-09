// <copyright file="SecretRedactorTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Xunit;

/// <summary>§10.6 — the redaction rules themselves (name + value, nested shapes, short values).</summary>
public sealed class SecretRedactorTests
{
    private const string Mask = SecretRedactor.Mask;

    private static SecretRedactor Redactor(params (string Name, object? Value)[] variables)
    {
        var definition = new WorkflowDefinition(
            Guid.NewGuid(),
            "wf",
            null,
            new Version(1, 0),
            Arr<NodeDefinition>.Empty,
            Arr<ConnectionDefinition>.Empty,
            HashMap<string, VariableDefinition>.Empty
                .Add("token", new VariableDefinition("token", PropertyType.String, null, IsSecret: true))
                .Add("pin", new VariableDefinition("pin", PropertyType.String, null, IsSecret: true))
                .Add("plain", new VariableDefinition("plain", PropertyType.String, null)));
        var pairs = new List<KeyValuePair<string, object?>>();
        foreach (var (name, value) in variables)
        {
            pairs.Add(new KeyValuePair<string, object?>(name, value));
        }

        return SecretRedactor.For(definition, pairs);
    }

    [Fact]
    public void SecretNamedKeys_AreMasked_AtAnyDepth_CaseInsensitively()
    {
        var redactor = Redactor(("token", "abcd-secret"));

        var result = redactor.Map(new Dictionary<string, object?>
        {
            ["TOKEN"] = "anything",
            ["nested"] = new Dictionary<string, object?> { ["token"] = 42, ["ok"] = "fine" },
            ["plain"] = "visible",
        })!;

        result["TOKEN"].Should().Be(Mask);
        ((Dictionary<string, object?>)result["nested"]!)["token"].Should().Be(Mask);
        ((Dictionary<string, object?>)result["nested"]!)["ok"].Should().Be("fine");
        result["plain"].Should().Be("visible");
    }

    [Fact]
    public void SecretValues_AreReplacedInsideStrings_ListsAndJson()
    {
        var redactor = Redactor(("token", "abcd-secret"));
        var json = JsonSerializer.SerializeToElement(new { url = "https://x/?k=abcd-secret", token = "abcd-secret", n = 1, list = new[] { "abcd-secret!" } });

        redactor.Text("Bearer abcd-secret failed").Should().Be($"Bearer {Mask} failed");
        redactor.Value(new List<object?> { "x abcd-secret", 3 }).Should().BeEquivalentTo(new List<object?> { $"x {Mask}", 3 });

        var redacted = (JsonElement)redactor.Value(json)!;
        redacted.GetProperty("url").GetString().Should().Be($"https://x/?k={Mask}");
        redacted.GetProperty("token").GetString().Should().Be(Mask);
        redacted.GetProperty("n").GetInt32().Should().Be(1);
        redacted.GetProperty("list")[0].GetString().Should().Be($"{Mask}!");
    }

    [Fact]
    public void ShortSecretValues_AreMaskedByNameOnly()
    {
        var redactor = Redactor(("pin", "12"));

        redactor.Text("order 12 shipped").Should().Be("order 12 shipped", because: "a 2-character value would blank out unrelated text");
        redactor.Map(new Dictionary<string, object?> { ["pin"] = "12" })!["pin"].Should().Be(Mask);
    }

    [Fact]
    public void ANewValue_IsLearned_AndTheOldOneStaysHidden()
    {
        var redactor = Redactor(("token", "first-secret")).With("token", "second-secret").With("plain", "not-a-secret");

        redactor.Text("first-secret second-secret not-a-secret").Should().Be($"{Mask} {Mask} not-a-secret");
    }

    [Fact]
    public void LongerSecretsWin_WhenOneContainsAnother()
    {
        var redactor = Redactor(("token", "abcd"), ("pin", "abcd-efgh"));

        redactor.Text("abcd-efgh").Should().Be(Mask);
    }

    [Fact]
    public void NoSecretsDeclared_ChangesNothing()
    {
        var redactor = SecretRedactor.For(
            new WorkflowDefinition(Guid.NewGuid(), "wf", null, new Version(1, 0), Arr<NodeDefinition>.Empty, Arr<ConnectionDefinition>.Empty, HashMap<string, VariableDefinition>.Empty),
            null);

        redactor.Should().BeSameAs(SecretRedactor.None);
        redactor.Text("anything").Should().Be("anything");
    }

    [Fact]
    public void Errors_KeepTheirMeaning()
    {
        var redactor = Redactor(("token", "abcd-secret"));
        var unchanged = new InvalidOperationException("nothing here");

        ExecutionSecrets.Error(redactor, unchanged).Should().BeSameAs(unchanged);
        ExecutionSecrets.Error(redactor, new OperationCanceledException("cancelled abcd-secret"))
            .Should().BeOfType<OperationCanceledException>().Which.Message.Should().Be($"cancelled {Mask}");
        ExecutionSecrets.Error(redactor, new InvalidOperationException("bad abcd-secret"))
            .Message.Should().Be($"bad {Mask}");
    }
}
