using Erp.Kernel.Lists;

namespace Erp.Kernel.Tests;

/// <summary>The in-memory LIKE matcher keeps PostgreSQL's ILIKE semantics (with backslash escapes)
/// now that the pattern becomes a regular expression once per query instead of once per row.</summary>
public sealed class LikePatternTests
{
    [Theory]
    [InlineData("%nour%", "Al Noor", false)]
    [InlineData("%noor%", "Al NOOR Trading", true)]
    [InlineData("al%", "AL Noor", true)]
    [InlineData("al%", "The Al Noor", false)]
    [InlineData("_l noor", "Al Noor", true)]
    [InlineData("_l noor", "AAl Noor", false)]
    [InlineData("100\\%", "100%", true)]
    [InlineData("100\\%", "1000", false)]
    [InlineData("a\\_b", "a_b", true)]
    [InlineData("a\\_b", "axb", false)]
    [InlineData("c:\\\\x", "c:\\x", true)]
    [InlineData("%.*%", "a.*b", true)]
    [InlineData("%.*%", "ab", false)]
    [InlineData("%(x)%", "a(x)b", true)]
    [InlineData("%line two%", "line one\nline two", true)]
    [InlineData("%النور%", "شركة النور للتجارة", true)]
    [InlineData("%nur%", "شركة النور للتجارة", false)]
    [InlineData("", "", true)]
    [InlineData("", "x", false)]
    [InlineData("%", "", true)]
    public void Matches_like_ilike(string pattern, string value, bool expected) =>
        Assert.Equal(expected, new LikePattern(pattern).IsMatch(value));

    [Fact]
    public void A_missing_value_never_matches_and_the_pattern_reads_back() =>
        Assert.True(!new LikePattern("%").IsMatch(null) && new LikePattern("a\\%b").ToString() == "a\\%b");

    [Fact]
    public void One_pattern_matches_many_rows_the_same_way_from_many_threads()
    {
        var pattern = new LikePattern("%row 1%");
        var rows = Enumerable.Range(0, 20_000).Select(i => $"Row {i}").ToArray();
        var expected = rows.Count(r => r.Contains("row 1", StringComparison.OrdinalIgnoreCase));
        var counts = Enumerable.Range(0, 8).AsParallel().Select(_ => rows.Count(pattern.IsMatch)).ToArray();
        Assert.All(counts, c => Assert.Equal(expected, c));
    }
}
