// <copyright file="LinqStudioTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Linq;

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Tests.UI.Api;
using Workflow.UI.Client.Api;
using Workflow.UI.Client.Linq.State;
using Workflow.UI.Client.Services;
using ClientLinqStudio = Workflow.UI.Client.Pages.LinqStudio;
using Xunit;

/// <summary>
/// 🧪 Linq Studio (L2/L3) — bUnit tests for the <c>/linq-studio</c> authoring page: connection →
/// catalog listing, manual table definition, validate/preview against the authoring endpoints, and
/// the designer publish handoff. Monaco is faked (loose interop) so the edit surface is the
/// textarea fallback~ ✨.
/// </summary>
public sealed class LinqStudioTests : TestContext
{
    private readonly LinqStudioHandoff handoff = new();

    public LinqStudioTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new AuthState());
        this.Services.AddSingleton(new ToastService());
        this.Services.AddSingleton(this.handoff);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>Installs a routed fake for the connections/catalog/authoring endpoints~ 🌐.</summary>
    private FakeHttpMessageHandler UseDefaultHandler(
        string? validateJson = null,
        string? previewJson = null,
        string? compileJson = null)
    {
        var connectionSaved = false;
        var handler = new FakeHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Post && path == "/api/database/connections/")
            {
                connectionSaved = true;
                return Json("{\"id\":\"sq-new\",\"providerKey\":\"sqlite\",\"connectionString\":\"***\",\"displayName\":\"New SQLite\",\"enabled\":true}");
            }

            if (req.Method == HttpMethod.Delete && path.StartsWith("/api/database/connections/", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (path == "/api/database/connections/")
            {
                return Json(connectionSaved
                    ? "[{\"id\":\"pg-main\",\"providerKey\":\"postgres\",\"connectionString\":\"***\",\"displayName\":\"Main PG\",\"enabled\":true},{\"id\":\"sq-new\",\"providerKey\":\"sqlite\",\"connectionString\":\"***\",\"displayName\":\"New SQLite\",\"enabled\":true}]"
                    : "[{\"id\":\"pg-main\",\"providerKey\":\"postgres\",\"connectionString\":\"***\",\"displayName\":\"Main PG\",\"enabled\":true}]");
            }

            if (req.Method == HttpMethod.Get && path == "/api/database/catalog/sq-new")
            {
                return Json("[]");
            }

            if (req.Method == HttpMethod.Put && path.StartsWith("/api/database/catalog/pg-main/", StringComparison.Ordinal))
            {
                return Json("{\"tableName\":\"orders\",\"schema\":null,\"columns\":[{\"name\":\"total\",\"dataType\":\"numeric\",\"nullable\":false}],\"clrTypeName\":null,\"assemblyName\":null}");
            }

            if (req.Method == HttpMethod.Delete && path.StartsWith("/api/database/catalog/pg-main/", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (req.Method == HttpMethod.Get && path == "/api/database/catalog/pg-main")
            {
                return Json("[{\"tableName\":\"orders\",\"schema\":\"public\",\"columns\":[{\"name\":\"id\",\"dataType\":\"integer\",\"nullable\":false,\"isPrimaryKey\":true,\"isIdentity\":true},{\"name\":\"total\",\"dataType\":\"numeric\",\"nullable\":true}],\"clrTypeName\":null,\"assemblyName\":null}]");
            }

            if (path == "/api/database/linq/validate")
            {
                return Json(validateJson ?? "{\"success\":true,\"errors\":[],\"warnings\":[]}");
            }

            if (path == "/api/database/linq/preview")
            {
                return Json(previewJson ?? "{\"success\":true,\"rows\":[{\"id\":1,\"total\":9.5}],\"rowCount\":1,\"durationMs\":7,\"diagnostics\":[],\"statements\":[\"SELECT [o].[id], [o].[total] FROM [orders] [o]\"]}");
            }

            if (path == "/api/database/linq/preview-live")
            {
                return Json("{\"success\":true,\"rows\":[{\"id\":42,\"total\":100.0}],\"rowCount\":1,\"durationMs\":12,\"diagnostics\":[{\"id\":\"WFLINQ020\",\"severity\":\"Warning\",\"message\":\"Ran against the live connection inside a transaction that was rolled back\",\"line\":0,\"column\":0}],\"statements\":[\"SELECT \\\"o\\\".\\\"id\\\" FROM \\\"orders\\\" \\\"o\\\"\"]}");
            }

            if (path == "/api/database/linq/compile")
            {
                return Json(compileJson ?? "{\"compiledAssemblyKey\":\"compiled-modules/abc123.dll\"}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        this.Services.AddSingleton(new DatabaseLinqClient(handler.CreateClient()));
        return handler;
    }

    private IRenderedComponent<ClientLinqStudio> RenderStudio()
        => this.RenderComponent<ClientLinqStudio>();

    [Fact]
    public void Studio_LoadsConnections_AndListsCatalogOnSelect()
    {
        this.UseDefaultHandler();
        var cut = this.RenderStudio();

        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("Main PG"));

        cut.Find("[data-testid=lq-connection]").Change("pg-main");

        cut.WaitForAssertion(() =>
        {
            var table = cut.Find("[data-testid=lq-table-orders]");
            table.TextContent.Should().Contain("orders");
            table.TextContent.Should().Contain("id");
            table.TextContent.Should().Contain("total"); // verbatim column identifiers
        });
    }

    [Fact]
    public void Studio_SaveManualTable_PutsToCatalog_AndSelectsIt()
    {
        var fake = this.UseDefaultHandler();
        var cut = this.RenderStudio();
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("Main PG"));
        cut.Find("[data-testid=lq-connection]").Change("pg-main");
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-table-orders]").Should().NotBeNull());

        cut.Find("[data-testid=lq-newtable-name]").Input("orders");
        cut.Find("[data-testid=lq-col-name-0]").Input("total");
        cut.Find("[data-testid=lq-newtable-save]").Click();

        cut.WaitForAssertion(() =>
        {
            fake.Requests.Should().Contain(r =>
                r.Method == HttpMethod.Put &&
                r.RequestUri!.AbsolutePath == "/api/database/catalog/pg-main/orders");
            cut.Instance.SelectedTables.Should().Contain("orders");
        });
    }

    [Fact]
    public void Studio_Validate_ShowsDiagnostics_WithLineInfo()
    {
        this.UseDefaultHandler(validateJson:
            "{\"success\":false,\"errors\":[{\"id\":\"CS1002\",\"severity\":\"Error\",\"message\":\"; expected\",\"line\":3,\"column\":12}],\"warnings\":[]}");
        var cut = this.RenderStudio();
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("Main PG"));

        cut.Find("[data-testid=lq-validate]").Click();

        cut.WaitForAssertion(() =>
        {
            var diags = cut.Find("[data-testid=lq-diags]");
            diags.TextContent.Should().Contain("CS1002");
            diags.TextContent.Should().Contain("(3,12)");
            diags.TextContent.Should().Contain("; expected");
        });
        cut.FindAll("[data-testid=lq-ok]").Should().BeEmpty();
    }

    [Fact]
    public void Studio_Validate_Success_ShowsOkBadge()
    {
        this.UseDefaultHandler();
        var cut = this.RenderStudio();
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("Main PG"));

        cut.Find("[data-testid=lq-validate]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-ok]").TextContent.Should().Contain("Compiles cleanly"));
    }

    [Fact]
    public void Studio_Preview_RendersSampleRows()
    {
        this.UseDefaultHandler();
        var cut = this.RenderStudio();
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("Main PG"));

        cut.Find("[data-testid=lq-preview]").Click();

        cut.WaitForAssertion(() =>
        {
            var grid = cut.Find("[data-testid=lq-preview-grid]");
            grid.InnerHtml.Should().Contain("total");
            grid.InnerHtml.Should().Contain("9.5");
        });
    }

    [Fact]
    public void Studio_Publish_CompilesAndFulfillsHandoff()
    {
        var fake = this.UseDefaultHandler();
        this.handoff.Request(
            nodeId: "n1",
            definitionId: "def-1",
            code: "return db.orders.ToList();",
            connectionId: "pg-main",
            tableNames: new[] { "orders" },
            returnUrl: "/designer/def-1");

        var cut = this.RenderStudio();
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("Main PG"));

        // Seeded from the handoff: table pre-selected, publish enabled~
        cut.Find("[data-testid=lq-publish]").HasAttribute("disabled").Should().BeFalse();
        cut.Instance.SelectedTables.Should().Contain("orders");

        cut.Find("[data-testid=lq-publish]").Click();

        cut.WaitForAssertion(() =>
        {
            fake.Requests.Should().Contain(r => r.RequestUri!.AbsolutePath == "/api/database/linq/compile");
            this.handoff.HasResult.Should().BeTrue();
        });

        var result = this.handoff.TakeResult();
        result.Should().NotBeNull();
        result!.Value.NodeId.Should().Be("n1");
        result.Value.Properties["compiledAssemblyKey"].GetString().Should().Be("compiled-modules/abc123.dll");
        result.Value.Properties["userCode"].GetString().Should().Contain("db.orders");
        result.Value.Properties["connectionId"].GetString().Should().Be("pg-main");

        var nav = this.Services.GetRequiredService<NavigationManager>();
        nav.Uri.Should().EndWith("/designer/def-1");
    }

    [Fact]
    public void Studio_Publish_Disabled_WithoutNodeContext()
    {
        this.UseDefaultHandler();
        var cut = this.RenderStudio();

        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("Main PG"));
        cut.Find("[data-testid=lq-publish]").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Studio_ReferencePanel_ListsSelectedTables_AndInsertsVerbatimIdentifiers()
    {
        this.UseDefaultHandler();
        var cut = this.RenderStudio();
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("Main PG"));
        cut.Find("[data-testid=lq-connection]").Change("pg-main");
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-table-orders]").Should().NotBeNull());

        // No reference panel until a table is selected~
        cut.FindAll("[data-testid=lq-ref]").Should().BeEmpty();

        cut.Find("[data-testid=lq-table-orders] input[type=checkbox]").Change(true);

        cut.WaitForAssertion(() =>
        {
            var refPanel = cut.Find("[data-testid=lq-ref]");
            refPanel.TextContent.Should().Contain("db.orders");
            refPanel.TextContent.Should().Contain(".total"); // verbatim, no pascal-casing
        });

        // Click-to-insert appends the identifier into the (fallback textarea) editor~
        cut.Find("[data-testid=lq-ref-table-orders]").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid=code-textarea]").GetAttribute("value").Should().Contain("db.orders"));

        cut.Find("[data-testid=lq-ref-col-orders-total]").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid=code-textarea]").GetAttribute("value").Should().Contain("total"));
    }

    [Fact]
    public void Studio_ReferencePanel_OffersTheRowTypeConstructor_WithoutANamespacePrefix()
    {
        // L7: the row type is aliased to the table's own name — the chip inserts exactly that~
        this.UseDefaultHandler();
        var cut = this.RenderStudio();
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("Main PG"));
        cut.Find("[data-testid=lq-connection]").Change("pg-main");
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-table-orders]").Should().NotBeNull());
        cut.Find("[data-testid=lq-table-orders] input[type=checkbox]").Change(true);

        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-ref-new-orders]").Should().NotBeNull());
        cut.Find("[data-testid=lq-ref-new-orders]").Click();

        cut.WaitForAssertion(() =>
        {
            var code = cut.Find("[data-testid=code-textarea]").GetAttribute("value");
            code.Should().Contain("new orders {");
            code.Should().NotContain("Gen_");
            code.Should().NotContain("WorkflowRuntime");
        });
    }

    [Fact]
    public void Studio_EditTable_LoadsDefinitionIntoTheForm_AndUpsertsChanges()
    {
        // L8: editing an existing definition instead of remove-and-recreate~
        var fake = this.UseDefaultHandler();
        var cut = this.RenderStudio();
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("Main PG"));
        cut.Find("[data-testid=lq-connection]").Change("pg-main");
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-table-orders]").Should().NotBeNull());

        cut.Find("[data-testid=lq-table-edit-orders]").Click();

        // The form is seeded with the catalogued definition~
        cut.Find("[data-testid=lq-tableform-title]").TextContent.Should().Contain("Edit table 'orders'");
        cut.Find("[data-testid=lq-newtable-name]").GetAttribute("value").Should().Be("orders");
        cut.Find("[data-testid=lq-newtable-schema]").GetAttribute("value").Should().Be("public");
        cut.Find("[data-testid=lq-col-name-0]").GetAttribute("value").Should().Be("id");
        cut.Find("[data-testid=lq-col-name-1]").GetAttribute("value").Should().Be("total");

        // Change a column type and add one, then save.
        cut.Find("[data-testid=lq-col-type-1]").Change("bigint");
        cut.Find("[data-testid=lq-col-add]").Click();
        cut.Find("[data-testid=lq-col-name-2]").Input("note");
        cut.Find("[data-testid=lq-newtable-save]").Click();

        cut.WaitForAssertion(() =>
        {
            fake.Requests.Should().Contain(r =>
                r.Method == HttpMethod.Put && r.RequestUri!.AbsolutePath == "/api/database/catalog/pg-main/orders");
            fake.Bodies.Should().Contain(b => b.Contains("bigint") && b.Contains("note") && b.Contains("public"));

            // No delete for a same-name edit, and the form returns to "define" mode.
            fake.Requests.Should().NotContain(r => r.Method == HttpMethod.Delete);
            cut.Find("[data-testid=lq-tableform-title]").TextContent.Should().Contain("Define a table");
        });
    }

    [Fact]
    public void Studio_EditTable_Rename_UpsertsNewAndRemovesOld_AndFollowsSelection()
    {
        var fake = this.UseDefaultHandler();
        var cut = this.RenderStudio();
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("Main PG"));
        cut.Find("[data-testid=lq-connection]").Change("pg-main");
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-table-orders]").Should().NotBeNull());

        cut.Find("[data-testid=lq-table-orders] input[type=checkbox]").Change(true);
        cut.Find("[data-testid=lq-table-edit-orders]").Click();
        cut.Find("[data-testid=lq-newtable-name]").Input("sales_orders");
        cut.Find("[data-testid=lq-newtable-save]").Click();

        cut.WaitForAssertion(() =>
        {
            fake.Requests.Should().Contain(r =>
                r.Method == HttpMethod.Put && r.RequestUri!.AbsolutePath == "/api/database/catalog/pg-main/sales_orders");
            fake.Requests.Should().Contain(r =>
                r.Method == HttpMethod.Delete && r.RequestUri!.AbsolutePath == "/api/database/catalog/pg-main/orders");
            cut.Instance.SelectedTables.Should().Contain("sales_orders").And.NotContain("orders");
        });
    }

    [Fact]
    public void Studio_EditTable_Cancel_ReturnsToDefineMode()
    {
        var fake = this.UseDefaultHandler();
        var cut = this.RenderStudio();
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("Main PG"));
        cut.Find("[data-testid=lq-connection]").Change("pg-main");
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-table-orders]").Should().NotBeNull());

        cut.Find("[data-testid=lq-table-edit-orders]").Click();
        cut.Find("[data-testid=lq-newtable-cancel]").Click();

        cut.Find("[data-testid=lq-tableform-title]").TextContent.Should().Contain("Define a table");
        cut.Find("[data-testid=lq-newtable-name]").GetAttribute("value").Should().BeNullOrEmpty();
        cut.FindAll("[data-testid=lq-newtable-cancel]").Should().BeEmpty();
        fake.Requests.Should().NotContain(r => r.Method == HttpMethod.Put);
    }

    [Fact]
    public void Studio_Preview_ShowsGeneratedSql()
    {
        // L9b: the SQL panel renders the statements the body produced~
        this.UseDefaultHandler();
        var cut = this.RenderStudio();
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("Main PG"));

        cut.Find("[data-testid=lq-preview]").Click();

        cut.WaitForAssertion(() =>
        {
            var sql = cut.Find("[data-testid=lq-sql]");
            sql.TextContent.Should().Contain("SELECT").And.Contain("orders");
        });
    }

    [Fact]
    public void Studio_LivePreview_PostsToPreviewLive_ShowsRollbackNoticeAndRows()
    {
        // L9c: running against the real connection, always rolled back~
        var fake = this.UseDefaultHandler();
        var cut = this.RenderStudio();
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("Main PG"));

        // Disabled until a connection is chosen — it targets a real database.
        cut.Find("[data-testid=lq-preview-live]").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid=lq-connection]").Change("pg-main");
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-preview-live]").HasAttribute("disabled").Should().BeFalse());

        cut.Find("[data-testid=lq-preview-live]").Click();

        cut.WaitForAssertion(() =>
        {
            fake.Requests.Should().Contain(r => r.RequestUri!.AbsolutePath == "/api/database/linq/preview-live");
            cut.Find("[data-testid=lq-preview-grid]").InnerHtml.Should().Contain("42");
            cut.Find("[data-testid=lq-diags]").TextContent.Should().Contain("rolled back");
            cut.Find("[data-testid=lq-sql]").TextContent.Should().Contain("SELECT");
        });
    }

    [Fact]
    public void Studio_DefineTable_IdentityColumn_ImpliesPrimaryKey_AndIsSaved()
    {
        // L9a: 🔑/⚡ toggles so InsertWithIdentity et al. work~
        var fake = this.UseDefaultHandler();
        var cut = this.RenderStudio();
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("Main PG"));
        cut.Find("[data-testid=lq-connection]").Change("pg-main");
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-table-orders]").Should().NotBeNull());

        cut.Find("[data-testid=lq-newtable-name]").Input("items");
        cut.Find("[data-testid=lq-col-name-0]").Input("id");
        cut.Find("[data-testid=lq-col-type-0]").Change("integer");
        cut.Find("[data-testid=lq-col-identity-0]").Change(true);

        // Identity implies a key column~
        cut.Find("[data-testid=lq-col-pk-0]").HasAttribute("checked").Should().BeTrue();

        cut.Find("[data-testid=lq-newtable-save]").Click();

        cut.WaitForAssertion(() => fake.Bodies.Should().Contain(b =>
            b.Contains("\"isIdentity\":true") && b.Contains("\"isPrimaryKey\":true")));
    }

    [Fact]
    public void Studio_CatalogList_MarksKeyAndIdentityColumns()
    {
        this.UseDefaultHandler();
        var cut = this.RenderStudio();
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("Main PG"));
        cut.Find("[data-testid=lq-connection]").Change("pg-main");

        cut.WaitForAssertion(() =>
        {
            var table = cut.Find("[data-testid=lq-table-orders]");
            table.TextContent.Should().Contain("🔑").And.Contain("⚡");
        });
    }

    [Fact]
    public void Studio_DeleteConnection_RemovesIt_AndClearsSelection()
    {
        // L10: saved connections are durable, so forgetting one is an explicit action~
        var fake = this.UseDefaultHandler();
        var cut = this.RenderStudio();
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("Main PG"));

        cut.Find("[data-testid=lq-conn-delete]").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid=lq-connection]").Change("pg-main");
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-conn-delete]").HasAttribute("disabled").Should().BeFalse());

        cut.Find("[data-testid=lq-conn-delete]").Click();

        cut.WaitForAssertion(() =>
        {
            fake.Requests.Should().Contain(r =>
                r.Method == HttpMethod.Delete && r.RequestUri!.AbsolutePath == "/api/database/connections/pg-main");
            cut.Find("[data-testid=lq-conn-delete]").HasAttribute("disabled").Should().BeTrue("the selection was cleared~");
        });
    }

    [Fact]
    public void Studio_NewConnection_GuidedPostgresForm_ComposesConnectionString()
    {
        var fake = this.UseDefaultHandler();
        var cut = this.RenderStudio();
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("Main PG"));

        cut.Find("[data-testid=lq-conn-new]").Click();
        cut.Find("[data-testid=lq-conn-id]").Input("pg-2");

        // Postgres is the default provider — guided fields, not a raw string box~
        cut.FindAll("[data-testid=lq-conn-cs]").Should().BeEmpty();
        cut.Find("[data-testid=lq-connf-Host]").Input("db.internal");
        cut.Find("[data-testid=lq-connf-Database]").Input("dotflow");
        cut.Find("[data-testid=lq-connf-Username]").Input("app");
        cut.Find("[data-testid=lq-connf-Password]").Input("s3cret");
        cut.Find("[data-testid=lq-connf-SSL-Mode]").Change("Require");

        // The preview shows the composed string with the secret masked.
        cut.Find("[data-testid=lq-conn-preview]").TextContent.Should().Contain("Host=db.internal").And.Contain("***");
        cut.Find("[data-testid=lq-conn-preview]").TextContent.Should().NotContain("s3cret");

        cut.Find("[data-testid=lq-conn-save]").Click();

        cut.WaitForAssertion(() => fake.Bodies.Should().Contain(b =>
            b.Contains("Host=db.internal") &&
            b.Contains("Database=dotflow") &&
            b.Contains("Username=app") &&
            b.Contains("s3cret") &&
            b.Contains("SSL Mode=Require") &&
            b.Contains("postgres")));
    }

    [Fact]
    public void Studio_NewConnection_SqliteProvider_SwapsToFileFields()
    {
        var fake = this.UseDefaultHandler();
        var cut = this.RenderStudio();
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("Main PG"));

        cut.Find("[data-testid=lq-conn-new]").Click();
        cut.Find("[data-testid=lq-conn-provider]").Change("sqlite");

        // Postgres-only fields disappear; the SQLite file path appears~
        cut.FindAll("[data-testid=lq-connf-Host]").Should().BeEmpty();
        cut.Find("[data-testid=lq-connf-Data-Source]").Should().NotBeNull();

        cut.Find("[data-testid=lq-conn-id]").Input("sq-new");
        cut.Find("[data-testid=lq-connf-Data-Source]").Input("./data/demo.db");
        cut.Find("[data-testid=lq-conn-save]").Click();

        cut.WaitForAssertion(() => fake.Bodies.Should().Contain(b =>
            b.Contains("Data Source=./data/demo.db") && b.Contains("sqlite")));
    }

    [Fact]
    public void Studio_NewConnection_RawModeToggle_CarriesComposedStringOver()
    {
        this.UseDefaultHandler();
        var cut = this.RenderStudio();
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("Main PG"));

        cut.Find("[data-testid=lq-conn-new]").Click();
        cut.Find("[data-testid=lq-connf-Host]").Input("db.internal");
        cut.Find("[data-testid=lq-connf-Database]").Input("dotflow");
        cut.Find("[data-testid=lq-connf-Username]").Input("app");

        cut.Find("[data-testid=lq-conn-raw]").Change(true);

        cut.Find("[data-testid=lq-conn-cs]").GetAttribute("value")
            .Should().Contain("Host=db.internal").And.Contain("Database=dotflow");
    }

    [Fact]
    public void Studio_NewConnection_RawMode_PostsWhatWasTyped()
    {
        var fake = this.UseDefaultHandler();
        var cut = this.RenderStudio();
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("Main PG"));

        cut.Find("[data-testid=lq-conn-new]").Click();
        cut.Find("[data-testid=lq-conn-id]").Input("sq-new");
        cut.Find("[data-testid=lq-conn-raw]").Change(true);
        cut.Find("[data-testid=lq-conn-cs]").Input("Host=raw;Database=x;Username=u");
        cut.Find("[data-testid=lq-conn-display]").Input("New SQLite");
        cut.Find("[data-testid=lq-conn-save]").Click();

        cut.WaitForAssertion(() =>
        {
            fake.Requests.Should().Contain(r =>
                r.Method == HttpMethod.Post &&
                r.RequestUri!.AbsolutePath == "/api/database/connections/");
            fake.Bodies.Should().Contain(b => b.Contains("Host=raw;Database=x;Username=u"));

            // Modal closed, list refreshed, the new connection selected + its (empty) catalog loaded~
            cut.FindAll("[data-testid=lq-conn-modal]").Should().BeEmpty();
            cut.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("New SQLite");
            cut.Find("[data-testid=lq-no-tables]").Should().NotBeNull();
        });
    }

    [Fact]
    public void Studio_NewConnection_RequiresIdAndRequiredProviderFields()
    {
        var fake = this.UseDefaultHandler();
        var cut = this.RenderStudio();
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("Main PG"));

        cut.Find("[data-testid=lq-conn-new]").Click();
        cut.Find("[data-testid=lq-conn-save]").Click();

        // No POST happened; the modal stays open for correction~
        fake.Requests.Should().NotContain(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/api/database/connections/");
        cut.Find("[data-testid=lq-conn-modal]").Should().NotBeNull();

        // An id alone isn't enough — Host/Database/Username are still required for postgres.
        cut.Find("[data-testid=lq-conn-id]").Input("pg-2");
        cut.Find("[data-testid=lq-conn-save]").Click();
        fake.Requests.Should().NotContain(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/api/database/connections/");
    }

    [Fact]
    public void Studio_SandboxHint_ShownWithoutNodeContext_HiddenWithHandoff()
    {
        this.UseDefaultHandler();
        var cut = this.RenderStudio();
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-sandbox-hint]").TextContent.Should().Contain("Sandbox mode"));

        this.DisposeComponents();
        this.handoff.Request("n1", "def-1", "return db.orders.ToList();", "pg-main", new[] { "orders" }, "/designer/def-1");
        var cut2 = this.RenderStudio();
        cut2.WaitForAssertion(() => cut2.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("Main PG"));
        cut2.FindAll("[data-testid=lq-sandbox-hint]").Should().BeEmpty();
    }

    // ── 🅾️ Oracle (L11c) ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Studio_NewConnection_OracleProvider_ComposesDataSource()
    {
        var fake = this.UseOracleHandler();
        var cut = this.RenderStudio();
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("Oracle Prod"));

        cut.Find("[data-testid=lq-conn-new]").Click();
        cut.Find("[data-testid=lq-conn-provider]").InnerHtml.Should().Contain("Oracle");
        cut.Find("[data-testid=lq-conn-provider]").Change("oracle");

        // Oracle's guided fields — no Postgres "Database" box~
        cut.FindAll("[data-testid=lq-connf-Database]").Should().BeEmpty();
        cut.Find("[data-testid=lq-conn-id]").Input("ora-2");
        cut.Find("[data-testid=lq-connf-Host]").Input("oracle.internal");
        cut.Find("[data-testid=lq-connf-Service-Name]").Input("ORCLPDB1");
        cut.Find("[data-testid=lq-connf-User-Id]").Input("APP");
        cut.Find("[data-testid=lq-conn-preview]").TextContent.Should().Contain("Data Source=oracle.internal:1521/ORCLPDB1");

        cut.Find("[data-testid=lq-conn-save]").Click();

        cut.WaitForAssertion(() => fake.Bodies.Should().Contain(b =>
            b.Contains("Data Source=oracle.internal:1521/ORCLPDB1") && b.Contains("oracle")));
    }

    [Fact]
    public void Studio_OracleConnectionSelected_TableDesignerOffersOracleTypes()
    {
        this.UseOracleHandler();
        var cut = this.RenderStudio();
        cut.WaitForAssertion(() => cut.Find("[data-testid=lq-connection]").InnerHtml.Should().Contain("Oracle Prod"));

        cut.Find("[data-testid=lq-connection]").Change("ora-main");

        cut.WaitForAssertion(() =>
        {
            var types = cut.Find("[data-testid=lq-col-type-0]").InnerHtml;
            types.Should().Contain("NUMBER").And.Contain("VARCHAR2");
            types.Should().NotContain(">text<");
        });
    }

    /// <summary>A fake whose only connection is an Oracle one~ 🅾️.</summary>
    private FakeHttpMessageHandler UseOracleHandler()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Post && path == "/api/database/connections/")
            {
                return Json("{\"id\":\"ora-2\",\"providerKey\":\"oracle\",\"connectionString\":\"***\",\"displayName\":\"Oracle 2\",\"enabled\":true}");
            }

            if (path == "/api/database/connections/")
            {
                return Json("[{\"id\":\"ora-main\",\"providerKey\":\"oracle\",\"connectionString\":\"***\",\"displayName\":\"Oracle Prod\",\"enabled\":true}]");
            }

            if (req.Method == HttpMethod.Get && path.StartsWith("/api/database/catalog/", StringComparison.Ordinal))
            {
                return Json("[]");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        this.Services.AddSingleton(new DatabaseLinqClient(handler.CreateClient()));
        return handler;
    }
}
