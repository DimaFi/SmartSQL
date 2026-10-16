using SmartSQL.Desktop.Queries;
using Xunit;

namespace SmartSQL.Desktop.Tests;

public sealed class SqlSyntaxHighlightingTests
{
    [Fact]
    public void KeywordsInsideStringsQuotedIdentifiersAndNestedCommentsAreNotCode()
    {
        const string text = "SELECT 'FROM ''WHERE''', \"SELECT\", [FROM], `WHERE` -- SELECT 42\r\n/* outer SELECT /* inner */ WHERE */ FROM items";
        var spans = SqlSyntaxHighlighting.Scan(text);
        Assert.Equal(new[] { "SELECT", "FROM" }, Fragments(text, spans, SqlHighlightKind.Keyword));
        Assert.Equal(new[] { "'FROM ''WHERE'''" }, Fragments(text, spans, SqlHighlightKind.String));
        Assert.Equal(new[] { "\"SELECT\"", "[FROM]", "`WHERE`" }, Fragments(text, spans, SqlHighlightKind.Identifier));
        Assert.Equal(new[] { "-- SELECT 42", "/* outer SELECT /* inner */ WHERE */" }, Fragments(text, spans, SqlHighlightKind.Comment));
        Assert.Empty(Fragments(text, spans, SqlHighlightKind.Number));
        AssertRanges(text, spans);
    }

    [Fact]
    public void PostgreSqlDollarQuotesEscapeStringsAndUnfinishedInputRemainEditableText()
    {
        const string text = "SELECT $body$SELECT; '🌍' -- anything$body$, $$WHERE$$, E'a\\'SELECT', 'unfinished SELECT\r\n🌍";
        var spans = SqlSyntaxHighlighting.Scan(text);
        Assert.Equal(new[] { "SELECT" }, Fragments(text, spans, SqlHighlightKind.Keyword));
        Assert.Equal(new[] { "$body$SELECT; '🌍' -- anything$body$", "$$WHERE$$", "'a\\'SELECT'", "'unfinished SELECT\r\n🌍" }, Fragments(text, spans, SqlHighlightKind.String));
        AssertRanges(text, spans);
    }

    [Fact]
    public void NativeNumericSpellingsAndKeywordBoundariesHaveExactUtf16Offsets()
    {
        const string text = "select selection, 1.25e-10, .5, 0xFF, 9223372036854775807, привет🌍 FROM items";
        var spans = SqlSyntaxHighlighting.Scan(text);
        Assert.Equal(new[] { "select", "FROM" }, Fragments(text, spans, SqlHighlightKind.Keyword));
        Assert.Equal(new[] { "1.25e-10", ".5", "0xFF", "9223372036854775807" }, Fragments(text, spans, SqlHighlightKind.Number));
        var from = Assert.Single(spans, span => text.Substring(span.Start, span.Length) == "FROM");
        Assert.Equal(text.IndexOf("FROM", StringComparison.Ordinal), from.Start);
        AssertRanges(text, spans);
    }

    [Fact]
    public void SelectionRemovesOverlappingSyntaxForegroundWithoutMovingAnyText()
    {
        var spans = new[] { new SqlHighlightSpan(0, 6, SqlHighlightKind.Keyword), new SqlHighlightSpan(8, 10, SqlHighlightKind.String) };
        var clipped = SqlSyntaxHighlighting.ExcludeSelection(spans, 3, 10);
        Assert.Equal(new[] { new SqlHighlightSpan(0, 3, SqlHighlightKind.Keyword), new SqlHighlightSpan(13, 5, SqlHighlightKind.String) }, clipped);
        Assert.Same(spans, SqlSyntaxHighlighting.ExcludeSelection(spans, 3, 0));
        Assert.Empty(SqlSyntaxHighlighting.ExcludeSelection(spans, 0, 100));
    }

    [Fact]
    public void WorkIsBoundedAndOversizedInputLosesOnlyDecoration()
    {
        var maximum = string.Join('+', Enumerable.Repeat("1", SqlSyntaxHighlighting.MaxCharacters / 2));
        var spans = SqlSyntaxHighlighting.Scan(maximum);
        Assert.Equal(SqlSyntaxHighlighting.MaxSpans, spans.Count);
        AssertRanges(maximum, spans);
        var oversized = new string('x', SqlSyntaxHighlighting.MaxCharacters + 1);
        Assert.Empty(SqlSyntaxHighlighting.Scan(oversized));
        Assert.Equal(SqlSyntaxHighlighting.MaxCharacters + 1, oversized.Length);
    }

    private static string[] Fragments(string text, IReadOnlyList<SqlHighlightSpan> spans, SqlHighlightKind kind) =>
        spans.Where(span => span.Kind == kind).Select(span => text.Substring(span.Start, span.Length)).ToArray();

    private static void AssertRanges(string text, IReadOnlyList<SqlHighlightSpan> spans)
    {
        var end = 0;
        foreach (var span in spans)
        {
            Assert.True(span.Start >= end);
            Assert.True(span.Length > 0);
            Assert.True(span.Start + span.Length <= text.Length);
            end = span.Start + span.Length;
        }
    }
}
