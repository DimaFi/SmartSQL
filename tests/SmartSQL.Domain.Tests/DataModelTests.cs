using SmartSQL.Domain;

namespace SmartSQL.Domain.Tests;

public sealed class DataModelTests
{
    [Xunit.Fact]
    public void TableDisplaysItsUserFacingName()
    {
        var table = new TableDefinition(Guid.NewGuid(), "Организации", []);
        Xunit.Assert.Equal("Организации", table.ToString());
    }
}
