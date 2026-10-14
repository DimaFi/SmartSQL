using System.Text.Json;
using SmartSQL.Domain;
using Xunit;

namespace SmartSQL.Application.Tests;

public sealed class DatabaseDeletionTests
{
    private static readonly DatabaseTableRef Table = new("Схема \" .", "таблица ;-- 🌍", DatabaseObjectKind.Table);

    [Fact]
    public void ConfirmationRequiresAHandleAndPreservesLegitimateQuotedNames()
    {
        var request = new DatabaseRowDelete(Table, Guid.NewGuid());
        request.Validate();
        (request with { Table = Table with { Name = " ", Schema = " " } }).Validate();
        Assert.Throws<ArgumentException>(() => (request with { RowHandle = Guid.Empty }).Validate());
        Assert.Throws<ArgumentNullException>(() => (request with { Table = null! }).Validate());
        Assert.Throws<ArgumentException>(() => (request with { Table = Table with { Kind = (DatabaseObjectKind)999 } }).Validate());
    }

    [Theory]
    [InlineData("")]
    [InlineData("a\0b")]
    public void InvalidIdentifiersAreRejectedBeforeProviderWork(string invalid)
    {
        Assert.Throws<ArgumentException>(() => new DatabaseRowDelete(Table with { Name = invalid }, Guid.NewGuid()).Validate());
        Assert.Throws<ArgumentException>(() => new DatabaseRowDelete(Table with { Schema = invalid }, Guid.NewGuid()).Validate());
    }

    [Fact]
    public void MalformedUnicodeIdentifiersAreRejectedWithoutReplacingCharacters()
    {
        foreach (var invalid in new[] { new string((char)0xD800, 1), new string((char)0xDC00, 1) })
        {
            Assert.Throws<ArgumentException>(() => new DatabaseRowDelete(Table with { Name = invalid }, Guid.NewGuid()).Validate());
            Assert.Throws<ArgumentException>(() => new DatabaseRowDelete(Table with { Schema = invalid }, Guid.NewGuid()).Validate());
        }
    }

    [Fact]
    public void DeletionHandleIsIndependentTransientAndDoesNotLeakThroughSerializationOrPrinting()
    {
        var ordinary = new DatabaseDisplayRow(["exact", null, string.Empty]);
        Assert.Null(ordinary.DeleteHandle);
        var row = ordinary with { DeleteHandle = Guid.NewGuid(), EditHandle = Guid.NewGuid() };
        var json = JsonSerializer.Serialize(row);
        Assert.DoesNotContain(row.DeleteHandle!.Value.ToString(), json);
        Assert.DoesNotContain(row.EditHandle!.Value.ToString(), json);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty(nameof(DatabaseDisplayRow.DeleteHandle), out _));
        Assert.False(document.RootElement.TryGetProperty(nameof(DatabaseDisplayRow.EditHandle), out _));
        Assert.Equal(ordinary.Values, JsonSerializer.Deserialize<DatabaseDisplayRow>(json)!.Values);
        Assert.DoesNotContain(row.DeleteHandle.Value.ToString(), row.ToString());
        var request = new DatabaseRowDelete(Table, row.DeleteHandle.Value);
        Assert.DoesNotContain(request.RowHandle.ToString(), request.ToString());
        Assert.True(typeof(IDatabaseBrowser).IsAssignableFrom(typeof(IDatabaseRowDeleter)));
        Assert.Equal(typeof(bool), typeof(IDatabaseRowDeleter).GetProperty(nameof(IDatabaseRowDeleter.EditingEnabled))!.PropertyType);
    }
}
