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
    public void FirstConnectionUsesTheOriginalFriendlyName()
    {
        var name = WorkspaceSourceLabels.UniqueDisplayName("Учёт практики", ["Другая база"]);
        Assert.Equal("Учёт практики", name);
    }

    [Fact]
    public void DuplicateNameGetsAnExplicitSessionQualifierAndDoesNotClashAgain()
    {
        var names = new[] { "Данные", "Данные (сеанс 2)", "DATA" };
        Assert.Equal("Данные (сеанс 3)", WorkspaceSourceLabels.UniqueDisplayName("Данные", names));
        Assert.Equal("data (сеанс 2)", WorkspaceSourceLabels.UniqueDisplayName("data", ["DATA"]));
    }

    [Fact]
    public void UnrelatedConnectionsNeverGetArbitrarySequenceNumbers()
    {
        Assert.Equal("Продажи", WorkspaceSourceLabels.UniqueDisplayName("Продажи", ["Сотрудники", "Клиенты"]));
    }

    [Fact]
    public void BlankNameAndNullActiveCollectionAreRejected()
    {
        Assert.Throws<ArgumentException>(() => WorkspaceSourceLabels.UniqueDisplayName(" ", []));
        Assert.Throws<ArgumentNullException>(() => WorkspaceSourceLabels.UniqueDisplayName("Имя", null!));
    }

    [Fact]
    public void AdvancedModeCannotGrantEditingAndLabelsDoNotClaimUnlimitedWrites()
    {
        Assert.Equal("Только чтение", WorkspaceSourceLabels.Access(false, false));
        Assert.Equal("Изменения по доступным операциям", WorkspaceSourceLabels.Access(false, true));
        Assert.Equal("Редактирование проекта", WorkspaceSourceLabels.Access(true, false));
    }
}
