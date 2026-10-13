using System.Security.Authentication;
using Npgsql;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Infrastructure.Tests;

public sealed class PostgresDatabaseBrowserTests
{
    [Xunit.Theory]
    [Xunit.InlineData("обычное", "\"обычное\"")]
    [Xunit.InlineData("Mixed Case 🌍", "\"Mixed Case 🌍\"")]
    [Xunit.InlineData("\"; DROP TABLE users; --", "\"\"\"; DROP TABLE users; --\"")]
    [Xunit.InlineData("line\nname", "\"line\nname\"")]
    [Xunit.InlineData("a.b", "\"a.b\"")]
    public void IdentifiersPreserveNamesAndKeepSqlInsideOneQuotedIdentifier(string input, string quoted)
    {
        Xunit.Assert.Equal(quoted, PostgresBrowserSql.QuoteIdentifier(input));
    }

    [Xunit.Fact]
    public void UnrepresentableIdentifiersFailBeforeAnyNetworkCall()
    {
        string[] inputs = [string.Empty, "a\0b", new string((char)0xD800, 1), new string((char)0xDC00, 1)];
        foreach (var input in inputs)
            Xunit.Assert.ThrowsAny<ArgumentException>(() => PostgresBrowserSql.QuoteIdentifier(input));
        Xunit.Assert.Equal("\"🌍\"", PostgresBrowserSql.QuoteIdentifier("🌍"));
    }

    [Xunit.Fact]
    public void PageQueryQuotesEveryCatalogIdentifierAndOrdersCompositeKeyInKeyOrder()
    {
        var table = new DatabaseTableRef("schema\"; SELECT 1; --", "rows\"; DELETE FROM x; --", DatabaseObjectKind.Table);
        DatabaseColumn[] columns =
        [
            new("value", 0, "numeric(100,30)", true, null),
            new("key two\"", 1, "bigint", false, 2),
            new("key one", 2, "text", false, 1)
        ];
        var sql = PostgresBrowserSql.Page(table, columns);
        Xunit.Assert.Contains("FROM \"schema\"\"; SELECT 1; --\".\"rows\"\"; DELETE FROM x; --\" AS q", sql);
        Xunit.Assert.Contains("q.\"value\"::pg_catalog.text", sql);
        Xunit.Assert.Contains("ORDER BY q.\"key one\", q.\"key two\"\"\" LIMIT $1 OFFSET $2", sql);
        Xunit.Assert.DoesNotContain("::numeric", sql);
        Xunit.Assert.DoesNotContain("ctid", sql);
    }

    [Xunit.Fact]
    public void KeylessNativeTypesUseTheirTextForOrderingWithoutRequiringNativeSortOperators()
    {
        var table = new DatabaseTableRef("public", "native", DatabaseObjectKind.View);
        DatabaseColumn[] columns = [new("json", 0, "json", true, null), new("xml", 1, "xml", true, null)];
        var sql = PostgresBrowserSql.Page(table, columns);
        Xunit.Assert.Contains("ORDER BY (q.\"json\"::pg_catalog.text) COLLATE pg_catalog.\"C\" NULLS FIRST", sql);
        Xunit.Assert.Contains("(q.\"xml\"::pg_catalog.text) COLLATE pg_catalog.\"C\" NULLS FIRST", sql);
        var diagnostics = PostgresBrowserSql.Diagnostics(table, columns, false, false);
        Xunit.Assert.Contains(diagnostics, item => item.Code == "no_primary_key");
        Xunit.Assert.Contains(diagnostics, item => item.Code == "unstable_pagination");
    }

    [Xunit.Fact]
    public void InheritanceAndRlsDoNotClaimUniversalReliableIdentityOrVisibility()
    {
        var table = new DatabaseTableRef("public", "parent", DatabaseObjectKind.Table);
        DatabaseColumn[] columns = [new("id", 0, "integer", false, 1)];
        var diagnostics = PostgresBrowserSql.Diagnostics(table, columns, true, true);
        Xunit.Assert.Contains(diagnostics, item => item.Code == "inherited_identity");
        Xunit.Assert.Contains(diagnostics, item => item.Code == "row_level_security");
        Xunit.Assert.Contains(diagnostics, item => item.Code == "live_pagination");
        Xunit.Assert.DoesNotContain(diagnostics, item => item.Code == "no_primary_key");
    }

    [Xunit.Fact]
    public void ConnectionSettingsEscapeProfileValuesAndNeverAllowThemToOverrideReadOnlyPolicy()
    {
        var profile = Profile() with
        {
            Database = "db;Options=-c default_transaction_read_only=off;Password=not-a-password",
            Username = "user;Pooling=true;Command Timeout=0"
        };
        profile.Validate();
        var generated = PostgresDatabaseBrowser.ConnectionSettings(profile);
        var settings = new NpgsqlConnectionStringBuilder(generated.ConnectionString);
        Xunit.Assert.Equal(profile.Database, settings.Database);
        Xunit.Assert.Equal(profile.Username, settings.Username);
        Xunit.Assert.Null(settings.Password);
        Xunit.Assert.False(settings.Pooling);
        Xunit.Assert.False(settings.Enlist);
        Xunit.Assert.False(settings.PersistSecurityInfo);
        Xunit.Assert.False(settings.IncludeErrorDetail);
        Xunit.Assert.False(settings.LogParameters);
        Xunit.Assert.Equal(15, settings.Timeout);
        Xunit.Assert.Equal(15, settings.CommandTimeout);
        Xunit.Assert.Contains("default_transaction_read_only=on", settings.Options);
        Xunit.Assert.Contains("statement_timeout=15000", settings.Options);
        Xunit.Assert.Equal(GssEncryptionMode.Disable, settings.GssEncryptionMode);
    }

    [Xunit.Theory]
    [Xunit.InlineData("28P01", "authentication_failed")]
    [Xunit.InlineData("28000", "authentication_failed")]
    [Xunit.InlineData("3D000", "database_unavailable")]
    [Xunit.InlineData("42501", "permission_denied")]
    [Xunit.InlineData("42P01", "object_changed")]
    [Xunit.InlineData("42703", "object_changed")]
    [Xunit.InlineData("57014", "operation_timed_out")]
    [Xunit.InlineData("55P03", "operation_timed_out")]
    [Xunit.InlineData("XX000", "server_error")]
    public void DriverDetailsAndSecretsCannotEscapeAsUserErrors(string sqlState, string expected)
    {
        const string secret = "Secret-Password-937;Host=private.example";
        var failure = new PostgresException(secret, "ERROR", "ERROR", sqlState, detail: secret, hint: secret);
        var error = PostgresDatabaseBrowser.Sanitize(failure);
        Xunit.Assert.Equal(expected, error.Code);
        Xunit.Assert.Null(error.InnerException);
        Xunit.Assert.DoesNotContain(secret, error.ToString());
    }

    [Xunit.Fact]
    public void NestedNetworkTlsAndTimeoutErrorsAreSanitized()
    {
        var tls = PostgresDatabaseBrowser.Sanitize(new NpgsqlException("secret", new AuthenticationException("another-secret")));
        Xunit.Assert.Equal("tls_failed", tls.Code);
        Xunit.Assert.DoesNotContain("secret", tls.ToString());
        var timeout = PostgresDatabaseBrowser.Sanitize(new NpgsqlException("secret", new TimeoutException("another-secret")));
        Xunit.Assert.Equal("operation_timed_out", timeout.Code);
        Xunit.Assert.Null(timeout.InnerException);
    }

    [Xunit.Fact]
    public async Task UnsupportedProviderAndPrecancelledConnectDoNotAttemptNetwork()
    {
        await Xunit.Assert.ThrowsAsync<ArgumentException>(() => PostgresDatabaseBrowser.OpenAsync(Profile() with { Provider = "another-provider" }, "password"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => PostgresDatabaseBrowser.OpenAsync(Profile(), "password", cancelled.Token));
    }

    [Xunit.Fact]
    public async Task LargeTextIsReadExactlyAtBoundaryAndRefusedExplicitlyBeyondIt()
    {
        const string expected = "🌍 ;\r\n _x0041_";
        using var source = new StringReader(expected);
        Xunit.Assert.Equal(expected, await PostgresDatabaseBrowser.ReadTextAsync(source, expected.Length, default));
        using var tooLarge = new StringReader(expected + "!");
        var error = await Xunit.Assert.ThrowsAsync<DatabaseBrowserException>(() => PostgresDatabaseBrowser.ReadTextAsync(tooLarge, expected.Length, default));
        Xunit.Assert.Equal("cell_too_large", error.Code);
        using var cancelledSource = new StringReader(expected);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => PostgresDatabaseBrowser.ReadTextAsync(cancelledSource, expected.Length, cancellation.Token));
    }

    private static ConnectionProfile Profile() => new(Guid.NewGuid(), "Test", DatabaseProviders.PostgreSql,
        "127.0.0.1", 15432, "smartsql_browser_test", "smartsql_browser_reader", ConnectionSslPolicy.Disable);
}
