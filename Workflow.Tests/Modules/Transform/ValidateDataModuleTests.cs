// <copyright file="ValidateDataModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Transform;

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Workflow.Modules.Builtin.Transform;
using Workflow.Modules.Validation;
using Xunit;

/// <summary>
/// ✅ Phase 2.6.a.4 — tests for <see cref="ValidateDataModule"/>~ ✨.
/// </summary>
public sealed class ValidateDataModuleTests : TransformModuleTestBase
{
    private readonly ValidateDataModule module = new();

    private static List<object?> Rules(params Dictionary<string, object?>[] rules)
        => new(rules);

    [Fact]
    public void ValidateModule_Metadata_IsCorrect()
    {
        this.module.ModuleId.Should().Be("builtin.transform.validate");
        new ModuleValidator().Validate(this.module).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Required_MissingField_Invalid()
    {
        var result = await this.module.ExecuteAsync(this.Context(
            new() { ["rules"] = Rules(Rec(("field", "name"), ("rule", "required"))) },
            new() { ["data"] = Rec(("age", 5L)) }));

        result.Outputs["isValid"].Should().Be(false);
    }

    [Fact]
    public async Task Type_Mismatch_Invalid()
    {
        var result = await this.module.ExecuteAsync(this.Context(
            new() { ["rules"] = Rules(Rec(("field", "age"), ("rule", "type"), ("value", "number"))) },
            new() { ["data"] = Rec(("age", "notanumber")) }));

        result.Outputs["isValid"].Should().Be(false);
    }

    [Fact]
    public async Task MinMaxLength_Enforced()
    {
        var result = await this.module.ExecuteAsync(this.Context(
            new() { ["rules"] = Rules(Rec(("field", "name"), ("rule", "minLength"), ("value", 3L))) },
            new() { ["data"] = Rec(("name", "ab")) }));

        result.Outputs["isValid"].Should().Be(false);
    }

    [Fact]
    public async Task NumericRange_Enforced()
    {
        var result = await this.module.ExecuteAsync(this.Context(
            new() { ["rules"] = Rules(Rec(("field", "age"), ("rule", "max"), ("value", 100L))) },
            new() { ["data"] = Rec(("age", 150L)) }));

        result.Outputs["isValid"].Should().Be(false);
    }

    [Fact]
    public async Task Pattern_Regex_Works()
    {
        var result = await this.module.ExecuteAsync(this.Context(
            new() { ["rules"] = Rules(Rec(("field", "code"), ("rule", "pattern"), ("value", "^[A-Z]{3}$"))) },
            new() { ["data"] = Rec(("code", "ABC")) }));

        result.Outputs["isValid"].Should().Be(true);
    }

    [Fact]
    public async Task Pattern_CatastrophicBacktracking_TimesOutSafely()
    {
        // Classic ReDoS pattern + a non-matching input that would hang a backtracking engine~ 🛡️
        var result = await this.module.ExecuteAsync(this.Context(
            new() { ["rules"] = Rules(Rec(("field", "s"), ("rule", "pattern"), ("value", "^(a+)+$"))) },
            new() { ["data"] = Rec(("s", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!")) }));

        // Must complete (not hang) — either invalid or a timeout-driven fail, but it returns~
        result.Should().NotBeNull();
        result.Outputs.Should().ContainKey("isValid");
    }

    [Fact]
    public async Task Email_And_Url_Formats()
    {
        var email = await this.module.ExecuteAsync(this.Context(
            new() { ["rules"] = Rules(Rec(("field", "e"), ("rule", "email"))) },
            new() { ["data"] = Rec(("e", "ada@example.com")) }));
        email.Outputs["isValid"].Should().Be(true);

        var url = await this.module.ExecuteAsync(this.Context(
            new() { ["rules"] = Rules(Rec(("field", "u"), ("rule", "url"))) },
            new() { ["data"] = Rec(("u", "not a url")) }));
        url.Outputs["isValid"].Should().Be(false);
    }

    [Fact]
    public async Task Enum_Membership()
    {
        var result = await this.module.ExecuteAsync(this.Context(
            new() { ["rules"] = Rules(Rec(("field", "color"), ("rule", "enum"), ("value", new List<object?> { "red", "green" }))) },
            new() { ["data"] = Rec(("color", "blue")) }));

        result.Outputs["isValid"].Should().Be(false);
    }

    [Fact]
    public async Task NestedField_DotPath_Validated()
    {
        var result = await this.module.ExecuteAsync(this.Context(
            new() { ["rules"] = Rules(Rec(("field", "user.email"), ("rule", "required"))) },
            new() { ["data"] = Rec(("user", Rec(("name", "Ada")))) }));

        result.Outputs["isValid"].Should().Be(false);
    }

    [Fact]
    public async Task ArrayInput_SplitsValidAndInvalid()
    {
        var data = new List<object?>
        {
            Rec(("name", "Ada")),
            Rec(("age", 5L)),
        };

        var result = await this.module.ExecuteAsync(this.Context(
            new() { ["rules"] = Rules(Rec(("field", "name"), ("rule", "required"))) },
            new() { ["data"] = data }));

        result.Outputs["isValid"].Should().Be(false);
        ((List<object?>)result.Outputs["validItems"]!).Should().HaveCount(1);
        ((List<object?>)result.Outputs["invalidItems"]!).Should().HaveCount(1);
    }

    [Fact]
    public async Task CustomExpressionRule_Works()
    {
        var result = await this.module.ExecuteAsync(this.Context(
            new() { ["rules"] = Rules(Rec(("field", "age"), ("rule", "custom"), ("value", "value >= 18"))) },
            new() { ["data"] = Rec(("age", 15L)) }));

        result.Outputs["isValid"].Should().Be(false);
    }

    [Fact]
    public async Task FailOnInvalid_ReturnsModuleFail()
    {
        var result = await this.module.ExecuteAsync(this.Context(
            new()
            {
                ["rules"] = Rules(Rec(("field", "name"), ("rule", "required"))),
                ["failOnInvalid"] = true,
            },
            new() { ["data"] = Rec(("age", 5L)) }));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task JsonSchemaMode_Validates()
    {
        var schema = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["required"] = new List<object?> { "name" },
            ["properties"] = new Dictionary<string, object?>
            {
                ["name"] = new Dictionary<string, object?> { ["type"] = "string" },
            },
        };

        var invalid = await this.module.ExecuteAsync(this.Context(
            new() { ["schema"] = schema },
            new() { ["data"] = Rec(("age", 5L)) }));
        invalid.Outputs["isValid"].Should().Be(false);

        var valid = await this.module.ExecuteAsync(this.Context(
            new() { ["schema"] = schema },
            new() { ["data"] = Rec(("name", "Ada")) }));
        valid.Outputs["isValid"].Should().Be(true);
    }

    [Fact]
    public void RulesAndSchema_MutuallyExclusive_FailsValidation()
    {
        this.module.ValidateConfiguration(new Dictionary<string, object?>
        {
            ["rules"] = new List<object?>(),
            ["schema"] = new Dictionary<string, object?>(),
        }).IsValid.Should().BeFalse();
    }
}
