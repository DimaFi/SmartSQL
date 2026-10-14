using System.Text.Json;
using SmartSQL.Domain;
using Xunit;

namespace SmartSQL.Application.Tests;

public sealed class DatabaseInsertionTests
{
    private static readonly DatabaseTableRef Table = new("public", "rows", DatabaseObjectKind.Table);
    private static InsertableDatabaseColumn Column(bool nullable = true, bool useDefault = true, bool mustDefault = false) =>
        new(new("note", 0, "text", DatabaseEditValueKind.Text, !mustDefault, nullable), useDefault, mustDefault);

    [Fact]
    public void ServerDefaultNullEmptyAndWhitespaceRemainDistinct()
    {
        foreach (var input in new[] { new DatabaseInsertValue("note", DatabaseInsertValueMode.ServerDefault),
                     new("note", DatabaseInsertValueMode.Null), new("note", DatabaseInsertValueMode.Value, ""),
                     new("note", DatabaseInsertValueMode.Value, "  \t\r\n  ") })
        {
            Assert.Equal(input, DatabaseInsertValueValidation.Normalize(Column(), input));
            new DatabaseInsertRequest(Table, Guid.NewGuid(), [input]).Validate();
        }
    }

    [Fact]
    public void GeneratedRequiredAndNonNullableRulesAreEnforced()
    {
        Assert.Throws<ArgumentException>(() => DatabaseInsertValueValidation.Normalize(Column(mustDefault: true), new("note", DatabaseInsertValueMode.Value, "x")));
        Assert.Throws<ArgumentException>(() => DatabaseInsertValueValidation.Normalize(Column(useDefault: false), new("note", DatabaseInsertValueMode.ServerDefault)));
        Assert.Throws<ArgumentException>(() => DatabaseInsertValueValidation.Normalize(Column(nullable: false), new("note", DatabaseInsertValueMode.Null)));
        Assert.Equal(DatabaseInsertValueMode.ServerDefault, DatabaseInsertValueValidation.Normalize(Column(mustDefault: true), new("note", DatabaseInsertValueMode.ServerDefault)).Mode);
    }

    [Fact]
    public void RequestsRejectDuplicatesInconsistentModesAndInvalidUnicode()
    {
        DatabaseInsertRequest Request(params DatabaseInsertValue[] inputs) => new(Table, Guid.NewGuid(), inputs);
        Assert.Throws<ArgumentException>(() => Request(new DatabaseInsertValue("note", DatabaseInsertValueMode.Value, null)).Validate());
        Assert.Throws<ArgumentException>(() => Request(new DatabaseInsertValue("note", DatabaseInsertValueMode.Null, "text")).Validate());
        Assert.Throws<ArgumentException>(() => Request(new DatabaseInsertValue("note", DatabaseInsertValueMode.Value, "a"), new("note", DatabaseInsertValueMode.Value, "b")).Validate());
        Assert.Throws<ArgumentException>(() => Request(new DatabaseInsertValue("note", (DatabaseInsertValueMode)999)).Validate());
        Assert.Throws<ArgumentException>(() => Request(new DatabaseInsertValue("note", DatabaseInsertValueMode.Value, "\0")).Validate());
        Assert.Throws<ArgumentException>(() => Request(new DatabaseInsertValue("note", DatabaseInsertValueMode.Value, new string((char)0xD800, 1))).Validate());
        Assert.Throws<ArgumentException>(() => (Request() with { ConfirmationHandle = Guid.Empty }).Validate());
        Request().Validate();
    }

    [Fact]
    public void InsertionPreservesHugeNumbersAndNormalizesBooleanWithoutSql()
    {
        const string exact = "1234567890123456789012345678901234567890.12345678901234567890";
        var number = Column() with { Column = new("note", 0, "numeric", DatabaseEditValueKind.Decimal, true, true) };
        Assert.Equal(exact, DatabaseInsertValueValidation.Normalize(number, new("note", DatabaseInsertValueMode.Value, exact)).Value);
        var boolean = number with { Column = number.Column with { Kind = DatabaseEditValueKind.Boolean } };
        Assert.Equal("true", DatabaseInsertValueValidation.Normalize(boolean, new("note", DatabaseInsertValueMode.Value, "Да")).Value);
    }

    [Fact]
    public void PrintingAndCapabilitySerializationOmitTransientTokensAndCellContent()
    {
        var handle = Guid.NewGuid();
        var input = new DatabaseInsertValue("note", DatabaseInsertValueMode.Value, "private value");
        var request = new DatabaseInsertRequest(Table, handle, [input]);
        var caps = new DatabaseInsertCapabilities(Table, true, [Column()], []) { ConfirmationHandle = handle };
        Assert.DoesNotContain(handle.ToString(), request.ToString());
        Assert.DoesNotContain(input.Value!, input.ToString());
        Assert.DoesNotContain(handle.ToString(), caps.ToString());
        Assert.DoesNotContain(nameof(caps.ConfirmationHandle), JsonSerializer.Serialize(caps));
    }
}
