using SmartSQL.Desktop.Views;
using Xunit;

namespace SmartSQL.Desktop.Tests;

public sealed class WorkspaceSourceLabelsTests
{
    [Theory]
    [InlineData(true, "SQLite", "Локальный проект SmartSQL")]
    [InlineData(false, "SQLite", "Существующий SQLite-файл")]
    [InlineData(false, "PostgreSQL", "Подключение к серверу PostgreSQL")]
    [InlineData(false, "unsupported", "Подключённая внешняя база данных")]
    public void KindDifferentiatesManagedAndOrdinarySources(bool managed, string provider, string expected)
    {
        Assert.Equal(expected, WorkspaceSourceLabels.Kind(managed, provider));
    }

    [Fact]
    public void AdvancedModeCannotGrantEditingAndLabelsDoNotClaimUnlimitedWrites()
    {
        Assert.Equal("Только чтение", WorkspaceSourceLabels.Access(false, false));
        Assert.Equal("Изменения по доступным операциям", WorkspaceSourceLabels.Access(false, true));
        Assert.Equal("Редактирование проекта", WorkspaceSourceLabels.Access(true, false));
    }
}
