using FM.Core.Search;
using Xunit;

namespace FM.Core.Tests;

public class NameMatcherTests
{
    [Theory]
    [InlineData("tài liệu", "tai lieu")]
    [InlineData("café", "cafe")]
    [InlineData("München", "Munchen")]
    [InlineData("Hà Nội Mùa Thu", "Ha Noi Mua Thu")]
    public void RemoveDiacritics_RemovesAccentsProperly(string input, string expected)
    {
        string result = NameMatcher.RemoveDiacritics(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("tài liệu quan trọng.docx", "tai lieu", true)]
    [InlineData("tài liệu quan trọng.docx", "trong tai", true)] // Order-independent tokens (AND)
    [InlineData("tài liệu quan trọng.docx", "DOCX TAI", true)]
    [InlineData("tài liệu quan trọng.docx", "bao cao", false)]
    [InlineData("simple_file.txt", "FILE SIMPLE", true)]
    [InlineData("simple_file.txt", "file notfound", false)]
    [InlineData("Anything.pdf", "", true)]
    [InlineData("Anything.pdf", "   ", true)]
    [InlineData(null, "test", false)]
    public void Matches_HandlesDiacriticsAndTokens(string? candidate, string? filter, bool expected)
    {
        bool result = NameMatcher.Matches(candidate, filter);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Score_RanksMatchesInExpectedHierarchy()
    {
        string query = "report";

        double exactScore = NameMatcher.Score("report", query);
        double prefixScore = NameMatcher.Score("reports_2026.pdf", query);
        double wordBoundaryScore = NameMatcher.Score("annual_report.docx", query);
        double substringScore = NameMatcher.Score("myreportfile.txt", query);
        double fuzzyScore = NameMatcher.Score("r_e_p_o_r_t.zip", query);
        double noMatchScore = NameMatcher.Score("completely_unrelated.jpg", query);

        Assert.True(exactScore > prefixScore, $"Exact ({exactScore}) should be > Prefix ({prefixScore})");
        Assert.True(prefixScore > wordBoundaryScore, $"Prefix ({prefixScore}) should be > WordBoundary ({wordBoundaryScore})");
        Assert.True(wordBoundaryScore > substringScore, $"WordBoundary ({wordBoundaryScore}) should be > Substring ({substringScore})");
        Assert.True(substringScore > fuzzyScore, $"Substring ({substringScore}) should be > Fuzzy ({fuzzyScore})");
        Assert.True(fuzzyScore > noMatchScore, $"Fuzzy ({fuzzyScore}) should be > NoMatch ({noMatchScore})");
        Assert.Equal(0, noMatchScore);
    }
}

