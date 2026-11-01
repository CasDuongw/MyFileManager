using FM.Core.Search;
using Xunit;

namespace FM.Core.Tests;

public class FuzzyMatcherTests
{
    [Fact]
    public void Match_TierHierarchy_RanksCorrectly()
    {
        string query = "report";

        var exact = FuzzyMatcher.Match("report", query);
        var prefix = FuzzyMatcher.Match("reports_2026", query);
        var wordStart = FuzzyMatcher.Match("annual_report", query);
        var substring = FuzzyMatcher.Match("myreportfile", query);
        var subsequence = FuzzyMatcher.Match("r_e_p_o_r_t", query);
        var noMatch = FuzzyMatcher.Match("completely_unrelated", query);

        Assert.NotNull(exact);
        Assert.NotNull(prefix);
        Assert.NotNull(wordStart);
        Assert.NotNull(substring);
        Assert.NotNull(subsequence);
        Assert.Null(noMatch);

        Assert.True(exact.Score > prefix.Score, $"Exact ({exact.Score}) > Prefix ({prefix.Score})");
        Assert.True(prefix.Score > wordStart.Score, $"Prefix ({prefix.Score}) > WordStart ({wordStart.Score})");
        Assert.True(wordStart.Score > substring.Score, $"WordStart ({wordStart.Score}) > Substring ({substring.Score})");
        Assert.True(substring.Score > subsequence.Score, $"Substring ({substring.Score}) > Subsequence ({subsequence.Score})");
    }

    [Theory]
    [InlineData("tài liệu dự án", "tai lieu", true)]
    [InlineData("Café Corner", "cafe", true)]
    [InlineData("München Reports", "munchen", true)]
    [InlineData("Đồ án", "do an", true)]
    [InlineData("Đồ án", "đo an", true)]
    [InlineData("Đại học", "dai hoc", true)]
    public void Match_DiacriticsAndCase_Insensitive(string candidate, string query, bool shouldMatch)
    {
        var result = FuzzyMatcher.Match(candidate, query);
        if (shouldMatch)
        {
            Assert.NotNull(result);
            Assert.True(result.Score > 0);
        }
        else
        {
            Assert.Null(result);
        }
    }

    [Fact]
    public void Match_MultiTokenQuery_RequiresAllTokens()
    {
        string candidate = "Documents 2026 Financial Report.xlsx";

        var match1 = FuzzyMatcher.Match(candidate, "doc report");
        var match2 = FuzzyMatcher.Match(candidate, "report 2026 doc");
        var fail = FuzzyMatcher.Match(candidate, "doc missing");

        Assert.NotNull(match1);
        Assert.NotNull(match2);
        Assert.Null(fail);
    }

    [Fact]
    public void Match_ReferenceBehavior_DoAndDocu()
    {
        var candidates = new[] { "Documents", "Downloads", "Desktop" };

        // Query "do" matches Documents (prefix), Downloads (prefix), Desktop (subsequence: D...o)
        var doResults = candidates
            .Select(c => new { Name = c, Match = FuzzyMatcher.Match(c, "do") })
            .Where(x => x.Match != null)
            .OrderByDescending(x => x.Match!.Score)
            .Select(x => x.Name)
            .ToList();

        Assert.Equal(3, doResults.Count);
        // Prefix matches should rank higher than subsequence match
        Assert.Contains("Documents", doResults.Take(2));
        Assert.Contains("Downloads", doResults.Take(2));
        Assert.Equal("Desktop", doResults[2]);

        // Query "docu" matches only Documents
        var docuResults = candidates
            .Select(c => new { Name = c, Match = FuzzyMatcher.Match(c, "docu") })
            .Where(x => x.Match != null)
            .Select(x => x.Name)
            .ToList();

        Assert.Single(docuResults);
        Assert.Equal("Documents", docuResults[0]);
    }

    [Fact]
    public void Match_NullAndEmptyQuery()
    {
        Assert.Null(FuzzyMatcher.Match(null, "query"));
        Assert.Null(FuzzyMatcher.Match("", "query"));

        var emptyQuery = FuzzyMatcher.Match("Documents", "");
        Assert.NotNull(emptyQuery);
        Assert.Equal(0.0, emptyQuery.Score);
    }
}

