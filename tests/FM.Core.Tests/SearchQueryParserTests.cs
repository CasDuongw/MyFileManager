using FM.Core.Models;
using FM.Core.Search;

namespace FM.Core.Tests;

public class SearchQueryParserTests
{
    [Fact]
    public void Parse_PlainText_ReturnsCleanQueryWithoutPredicatesOrWarnings()
    {
        var res = SearchQueryParser.Parse("annual financial report");

        Assert.True(res.IsValid);
        Assert.Equal("annual financial report", res.CleanQueryText);
        Assert.Null(res.FilterPredicate);
        Assert.Empty(res.Warnings);
    }

    [Fact]
    public void Parse_ExtOperator_FiltersBySpecifiedExtensions()
    {
        var res = SearchQueryParser.Parse("budget ext:pdf,xlsx");

        Assert.True(res.IsValid);
        Assert.Equal("budget", res.CleanQueryText);
        Assert.NotNull(res.FilterPredicate);

        var pdfEntry = new FileEntry("budget.pdf", @"C:\Docs\budget.pdf", false, 1024, DateTime.Now);
        var xlsxEntry = new FileEntry("budget.xlsx", @"C:\Docs\budget.xlsx", false, 2048, DateTime.Now);
        var docEntry = new FileEntry("budget.docx", @"C:\Docs\budget.docx", false, 4096, DateTime.Now);

        Assert.True(res.FilterPredicate(pdfEntry));
        Assert.True(res.FilterPredicate(xlsxEntry));
        Assert.False(res.FilterPredicate(docEntry));
    }

    [Fact]
    public void Parse_KindOperator_FolderAndImage_MatchesCategories()
    {
        var resFolder = SearchQueryParser.Parse("projects kind:folder");
        Assert.True(resFolder.IsValid);
        Assert.Equal("projects", resFolder.CleanQueryText);
        Assert.NotNull(resFolder.FilterPredicate);

        var dirEntry = new FileEntry("projects", @"C:\projects", true, 0, DateTime.Now);
        var fileEntry = new FileEntry("projects.txt", @"C:\projects.txt", false, 10, DateTime.Now);
        Assert.True(resFolder.FilterPredicate(dirEntry));
        Assert.False(resFolder.FilterPredicate(fileEntry));

        var resImage = SearchQueryParser.Parse("banner kind:image");
        Assert.True(resImage.IsValid);
        var pngEntry = new FileEntry("banner.png", @"C:\banner.png", false, 500, DateTime.Now);
        var mp3Entry = new FileEntry("banner.mp3", @"C:\banner.mp3", false, 500, DateTime.Now);
        Assert.True(resImage.FilterPredicate!(pngEntry));
        Assert.False(resImage.FilterPredicate(mp3Entry));
    }

    [Fact]
    public void Parse_SizeOperator_GreaterThanAndRange_MatchesFileSize()
    {
        var resGt = SearchQueryParser.Parse("video size:>5mb");
        Assert.True(resGt.IsValid);
        var bigFile = new FileEntry("movie.mp4", @"C:\movie.mp4", false, 6 * 1024 * 1024, DateTime.Now);
        var smallFile = new FileEntry("clip.mp4", @"C:\clip.mp4", false, 2 * 1024 * 1024, DateTime.Now);

        Assert.True(resGt.FilterPredicate!(bigFile));
        Assert.False(resGt.FilterPredicate(smallFile));

        var resRange = SearchQueryParser.Parse("data size:1mb..10mb");
        Assert.True(resRange.IsValid);
        var inRange = new FileEntry("data.bin", @"C:\data.bin", false, 5 * 1024 * 1024, DateTime.Now);
        var outRange = new FileEntry("huge.bin", @"C:\huge.bin", false, 20 * 1024 * 1024, DateTime.Now);
        Assert.True(resRange.FilterPredicate!(inRange));
        Assert.False(resRange.FilterPredicate(outRange));
    }

    [Fact]
    public void Parse_ModifiedOperator_Today_MatchesRecentDates()
    {
        var res = SearchQueryParser.Parse("notes modified:today");
        Assert.True(res.IsValid);

        var todayEntry = new FileEntry("today.txt", @"C:\today.txt", false, 100, DateTime.Now);
        var oldEntry = new FileEntry("old.txt", @"C:\old.txt", false, 100, DateTime.Now.AddDays(-10));

        Assert.True(res.FilterPredicate!(todayEntry));
        Assert.False(res.FilterPredicate(oldEntry));
    }

    [Fact]
    public void Parse_UnknownOperator_BecomesLiteralTextWithWarning()
    {
        var res = SearchQueryParser.Parse("quarterly tag:finance author:john");

        Assert.True(res.IsValid);
        Assert.Equal("quarterly tag:finance author:john", res.CleanQueryText);
        Assert.Equal(2, res.Warnings.Count);
        Assert.Contains(res.Warnings, w => w.Contains("tag:"));
        Assert.Contains(res.Warnings, w => w.Contains("author:"));
    }

    [Fact]
    public void Parse_InvalidSizeValue_ReturnsInvalidWithError()
    {
        var res = SearchQueryParser.Parse("report size:not_a_number");

        Assert.False(res.IsValid);
        Assert.NotNull(res.ErrorMessage);
        Assert.Contains("not_a_number", res.ErrorMessage);
    }

    [Fact]
    public void Parse_InvalidDateValue_ReturnsInvalidWithError()
    {
        var res = SearchQueryParser.Parse("report modified:invalid_date_expression");

        Assert.False(res.IsValid);
        Assert.NotNull(res.ErrorMessage);
        Assert.Contains("invalid_date_expression", res.ErrorMessage);
    }
}

