using SmartSQL.Application;
using Xunit;

namespace SmartSQL.Application.Tests;

public sealed class SqlReadRequestTests
{
    [Fact]
    public void SqlUsesUtf8ByteBoundaryAndRejectsInvalidUnicodeBeforeProviderWork()
    {
        new SqlReadRequest(new string('я', 32_768), 500).Validate();
        Assert.Throws<ArgumentException>(() => new SqlReadRequest(new string('я', 32_769)).Validate());
        foreach (var sql in new[] { "", " \r\n", "SELECT '\0'", "SELECT '\ud800'", "SELECT '\udc00'" })
            Assert.ThrowsAny<ArgumentException>(() => new SqlReadRequest(sql).Validate());
        new SqlReadRequest("SELECT '🌍; --'", 1).Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() => new SqlReadRequest("SELECT 1", 0).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new SqlReadRequest("SELECT 1", 501).Validate());
    }
}
