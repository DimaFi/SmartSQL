using SmartSQL.Desktop.Queries;
using Xunit;

namespace SmartSQL.Desktop.Tests;

public sealed class SqlTextSearchTests
{
    [Fact]
    public void NextAndWrapDoNotModifySql()
    {
        const string sql = "select a FROM t where a = 1; select b FROM t";
        var first = SqlTextSearch.Find(sql, "select", 0, 0);
        Assert.Equal(new SqlFindMatch(0, 6), first);

        var second = SqlTextSearch.Find(sql, "SELECT", first!.Value.Start, first.Value.Start + first.Value.Length);
        Assert.Equal(new SqlFindMatch(27, 6), second);

        var wrapped = SqlTextSearch.Find(sql, "select", second!.Value.Start, second.Value.Start + second.Value.Length);
        Assert.Equal(first, wrapped);
        Assert.Equal("select a FROM t where a = 1; select b FROM t", sql);
    }

    [Fact]
    public void PreviousWrapsBackToLastMatch()
    {
        const string sql = "SELECT x; SELECT y";
        Assert.Equal(new SqlFindMatch(10, 6), SqlTextSearch.Find(sql, "select", 0, 0, backwards: true));
        Assert.Equal(new SqlFindMatch(0, 6), SqlTextSearch.Find(sql, "SELECT", 10, 16, backwards: true));
    }

    [Fact]
    public void SelectionInReverseAndOutOfRangeCaretAreHandled()
    {
        const string sql = "one TWO two";
        Assert.Equal(new SqlFindMatch(8, 3), SqlTextSearch.Find(sql, "two", 7, 4));
        Assert.Equal(new SqlFindMatch(4, 3), SqlTextSearch.Find(sql, "two", 500, 500));
        Assert.Equal(new SqlFindMatch(8, 3), SqlTextSearch.Find(sql, "two", -10, -10, backwards: true));
    }

    [Fact]
    public void OffsetsAreUtf16NotUnicodeScalarIndexes()
    {
        const string sql = "🌍 SELECT 🌍 select";
        Assert.Equal(new SqlFindMatch(3, 6), SqlTextSearch.Find(sql, "select", 0, 0));
        Assert.Equal(new SqlFindMatch(13, 6), SqlTextSearch.Find(sql, "SELECT", 9, 9));
    }

    [Theory]
    [InlineData("", "SELECT")]
    [InlineData("SELECT", "")]
    [InlineData("SELECT", "nomatch")]
    public void EmptyAndMissingValuesHaveNoMatch(string sql, string search)
    {
        Assert.Null(SqlTextSearch.Find(sql, search, 0, 0));
    }

    [Fact]
    public void NullInputIsSafe()
    {
        Assert.Null(SqlTextSearch.Find(null, "a", 0, 0));
        Assert.Null(SqlTextSearch.Find("a", null, 0, 0));
    }

    [Fact]
    public void SpecialRegexCharactersAreMatchedLiterally()
    {
        const string sql = "select a.* from table";
        Assert.Equal(new SqlFindMatch(7, 3), SqlTextSearch.Find(sql, "a.*", 0, 0));
        Assert.Null(SqlTextSearch.Find(sql, "[a-z]+", 0, 0));
    }
}
