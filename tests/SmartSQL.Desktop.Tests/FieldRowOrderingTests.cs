using SmartSQL.Desktop.Views;

namespace SmartSQL.Desktop.Tests;

public sealed class FieldRowOrderingTests
{
    [Xunit.Fact]
    public void MovingMiddleFieldUpKeepsOtherFieldsInOrder()
    {
        var fields = new List<string> { "Имя", "Возраст", "Дата" };

        Xunit.Assert.True(FieldRowOrdering.MoveByOne(fields, 1, -1));

        Xunit.Assert.Equal(new[] { "Возраст", "Имя", "Дата" }, fields);
    }

    [Xunit.Fact]
    public void MovingMiddleFieldDownKeepsOriginalObjects()
    {
        var first = new object();
        var middle = new object();
        var last = new object();
        var fields = new List<object> { first, middle, last };

        Xunit.Assert.True(FieldRowOrdering.MoveByOne(fields, 1, 1));

        Xunit.Assert.Same(first, fields[0]);
        Xunit.Assert.Same(last, fields[1]);
        Xunit.Assert.Same(middle, fields[2]);
    }

    [Xunit.Theory]
    [Xunit.InlineData(0, -1)]
    [Xunit.InlineData(2, 1)]
    public void BoundaryMoveReturnsFalseWithoutChangingFields(int index, int direction)
    {
        var fields = new List<string> { "A", "B", "C" };

        Xunit.Assert.False(FieldRowOrdering.MoveByOne(fields, index, direction));

        Xunit.Assert.Equal(new[] { "A", "B", "C" }, fields);
    }

    [Xunit.Fact]
    public void OneFieldCannotMoveAndRemainsPresent()
    {
        var fields = new List<string> { "A" };

        Xunit.Assert.False(FieldRowOrdering.MoveByOne(fields, 0, -1));
        Xunit.Assert.False(FieldRowOrdering.MoveByOne(fields, 0, 1));
        Xunit.Assert.Single(fields);
    }

    [Xunit.Fact]
    public void InvalidIndexesAndDirectionsAreRejected()
    {
        var fields = new List<string> { "A", "B" };

        Xunit.Assert.Throws<ArgumentOutOfRangeException>(() => FieldRowOrdering.MoveByOne(fields, -1, 1));
        Xunit.Assert.Throws<ArgumentOutOfRangeException>(() => FieldRowOrdering.MoveByOne(fields, 2, -1));
        Xunit.Assert.Throws<ArgumentOutOfRangeException>(() => FieldRowOrdering.MoveByOne(fields, 0, 0));
        Xunit.Assert.Equal(new[] { "A", "B" }, fields);
    }

    [Xunit.Fact]
    public void NullCollectionIsRejected()
    {
        Xunit.Assert.Throws<ArgumentNullException>(
            () => FieldRowOrdering.MoveByOne<string>(null!, 0, 1));
    }
}
