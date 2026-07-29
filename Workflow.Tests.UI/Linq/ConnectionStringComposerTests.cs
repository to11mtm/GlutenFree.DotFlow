// <copyright file="ConnectionStringComposerTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Linq;

using System.Collections.Generic;
using FluentAssertions;
using Workflow.UI.Client.Linq.State;
using Xunit;

/// <summary>
/// 🧪 Linq Studio (L6) — the provider-aware connection-string composer: guided fields, validation,
/// composition, and round-tripping~ ✨.
/// </summary>
public sealed class ConnectionStringComposerTests
{
    [Theory]
    [InlineData("postgres", "Host")]
    [InlineData("sqlite", "Data Source")]
    public void FieldsFor_KnownProvider_LeadsWithTheDefiningSetting(string provider, string firstKey)
        => ConnectionStringComposer.FieldsFor(provider)[0].Key.Should().Be(firstKey);

    [Fact]
    public void FieldsFor_UnknownProvider_IsEmpty_SoTheUiFallsBackToRaw()
        => ConnectionStringComposer.FieldsFor("oracle").Should().BeEmpty();

    [Fact]
    public void Build_Postgres_ComposesRequiredFields_AndOmitsDefaults()
    {
        var values = ConnectionStringComposer.Defaults("postgres");
        values["Host"] = "db.internal";
        values["Database"] = "dotflow";
        values["Username"] = "app";
        values["Password"] = "s3cret";

        var cs = ConnectionStringComposer.Build("postgres", values);

        // Port/SSL Mode/Pooling/Timeout sit at their defaults → omitted for tidiness.
        cs.Should().Be("Host=db.internal;Database=dotflow;Username=app;Password=s3cret");
    }

    [Fact]
    public void Build_Postgres_IncludesNonDefaultOptionalSettings()
    {
        var values = ConnectionStringComposer.Defaults("postgres");
        values["Host"] = "db.internal";
        values["Database"] = "dotflow";
        values["Username"] = "app";
        values["Port"] = "6432";
        values["SSL Mode"] = "Require";

        var cs = ConnectionStringComposer.Build("postgres", values);

        cs.Should().Contain("Port=6432").And.Contain("SSL Mode=Require");
    }

    [Fact]
    public void Build_Sqlite_ComposesTheFilePath()
    {
        var values = ConnectionStringComposer.Defaults("sqlite");
        values["Data Source"] = "./data/dotflow.db";

        ConnectionStringComposer.Build("sqlite", values).Should().Be("Data Source=./data/dotflow.db");
    }

    [Fact]
    public void Build_QuotesValuesContainingSeparators()
    {
        var values = ConnectionStringComposer.Defaults("postgres");
        values["Host"] = "db";
        values["Database"] = "dotflow";
        values["Username"] = "app";
        values["Password"] = "pa;ss";

        ConnectionStringComposer.Build("postgres", values).Should().Contain("Password=\"pa;ss\"");
    }

    [Fact]
    public void MissingRequired_ReportsLabels_ForEmptyRequiredFields()
    {
        var values = ConnectionStringComposer.Defaults("postgres");
        values["Host"] = "db";

        ConnectionStringComposer.MissingRequired("postgres", values)
            .Should().BeEquivalentTo(new[] { "Database", "Username" });
    }

    [Fact]
    public void MissingRequired_Satisfied_IsEmpty()
    {
        var values = new Dictionary<string, string?>
        {
            ["Data Source"] = ":memory:",
        };

        ConnectionStringComposer.MissingRequired("sqlite", values).Should().BeEmpty();
    }

    [Fact]
    public void Parse_RoundTripsAComposedString()
    {
        var values = ConnectionStringComposer.Defaults("postgres");
        values["Host"] = "db.internal";
        values["Database"] = "dotflow";
        values["Username"] = "app";
        values["Password"] = "pa;ss";
        values["Port"] = "6432";

        var parsed = ConnectionStringComposer.Parse("postgres", ConnectionStringComposer.Build("postgres", values));

        parsed["Host"].Should().Be("db.internal");
        parsed["Database"].Should().Be("dotflow");
        parsed["Username"].Should().Be("app");
        parsed["Password"].Should().Be("pa;ss", because: "quoted values unquote cleanly~");
        parsed["Port"].Should().Be("6432");
    }

    [Fact]
    public void Parse_UnknownKeywords_AreIgnored_KnownOnesWin()
    {
        var parsed = ConnectionStringComposer.Parse("sqlite", "Data Source=x.db;Some Vendor Thing=42;Mode=ReadOnly");

        parsed["Data Source"].Should().Be("x.db");
        parsed["Mode"].Should().Be("ReadOnly");
        parsed.Should().NotContainKey("Some Vendor Thing");
    }

    [Fact]
    public void ProviderLabel_IsFriendly()
    {
        ConnectionStringComposer.ProviderLabel("postgres").Should().Be("PostgreSQL");
        ConnectionStringComposer.ProviderLabel("sqlite").Should().Be("SQLite");
    }
}
