using System.IO;
using FM.Core.FileSystem;
using FM.Core.Operations.MultiRename;
using Xunit;

namespace FM.Core.Tests;

public class MultiRenameTests
{
    private readonly InMemoryFileSystem _fs;

    public MultiRenameTests()
    {
        _fs = new InMemoryFileSystem();
        _fs.CreateDirectory(@"C:\TestDir");
    }

    [Fact]
    public void Evaluator_BasicTokens_EvaluatesExpectedStrings()
    {
        var item = new MultiRenameItemInput(
            FullPath: @"C:\TestDir\MyDocument.PDF",
            Name: "MyDocument.PDF",
            IsDirectory: false,
            DateModified: new DateTime(2026, 11, 3, 14, 30, 0));

        // 1. [N].[E] with Lower extension case
        var opts1 = new MultiRenameOptions("[N].[E]", ExtCase: ExtensionCase.Lower);
        string res1 = MultiRenameTokenEvaluator.Evaluate(item, 0, opts1);
        Assert.Equal("MyDocument.pdf", res1);

        // 2. Substring [N1-5]
        var opts2 = new MultiRenameOptions("[N1-5].[E]");
        string res2 = MultiRenameTokenEvaluator.Evaluate(item, 0, opts2);
        Assert.Equal("MyDoc.PDF", res2);

        // 3. Counter with 3-digit padding [C:001]
        var opts3 = new MultiRenameOptions("Doc_[C:001].[E]", CounterStart: 1, CounterPadding: 3);
        string res3 = MultiRenameTokenEvaluator.Evaluate(item, 4, opts3);
        Assert.Equal("Doc_005.PDF", res3);

        // 4. Date format [D:yyyyMMdd]
        var opts4 = new MultiRenameOptions("[N]_[D:yyyyMMdd].[E]");
        string res4 = MultiRenameTokenEvaluator.Evaluate(item, 0, opts4);
        Assert.Equal("MyDocument_20261103.PDF", res4);

        // 5. Extension Title Case
        var opts5 = new MultiRenameOptions("[N].[E]", ExtCase: ExtensionCase.Title);
        string res5 = MultiRenameTokenEvaluator.Evaluate(item, 0, opts5);
        Assert.Equal("MyDocument.Pdf", res5);
    }

    [Fact]
    public void Evaluator_DirectoryItem_RemovesDotExtensionToken()
    {
        var dirItem = new MultiRenameItemInput(
            FullPath: @"C:\TestDir\Projects",
            Name: "Projects",
            IsDirectory: true,
            DateModified: DateTime.UtcNow);

        var opts = new MultiRenameOptions("[N].[E]");
        string result = MultiRenameTokenEvaluator.Evaluate(dirItem, 0, opts);

        // Should NOT produce trailing dot "Projects."
        Assert.Equal("Projects", result);
    }

    [Fact]
    public void Evaluator_FindAndReplace_WorksWithPlainAndRegex()
    {
        var item = new MultiRenameItemInput(
            FullPath: @"C:\TestDir\IMG_2026_01.jpg",
            Name: "IMG_2026_01.jpg",
            IsDirectory: false,
            DateModified: DateTime.UtcNow);

        // Plain string replacement
        var optsPlain = new MultiRenameOptions(
            Pattern: "[N].[E]",
            FindText: "IMG",
            ReplaceText: "Photo");
        string resPlain = MultiRenameTokenEvaluator.Evaluate(item, 0, optsPlain);
        Assert.Equal("Photo_2026_01.jpg", resPlain);

        // Regex replacement
        var optsRegex = new MultiRenameOptions(
            Pattern: "[N].[E]",
            FindText: @"_01(?=\.)",
            ReplaceText: "_NEW",
            UseRegex: true);
        string resRegex = MultiRenameTokenEvaluator.Evaluate(item, 0, optsRegex);
        Assert.Equal("IMG_2026_NEW.jpg", resRegex);
    }

    [Fact]
    public void Planner_ValidatesFileNames_RejectsInvalidWin32Names()
    {
        var items = new[]
        {
            new MultiRenameItemInput(@"C:\TestDir\file1.txt", "file1.txt", false, DateTime.UtcNow),
            new MultiRenameItemInput(@"C:\TestDir\file2.txt", "file2.txt", false, DateTime.UtcNow),
            new MultiRenameItemInput(@"C:\TestDir\file3.txt", "file3.txt", false, DateTime.UtcNow)
        };

        // Pattern creating invalid chars: "Bad:Name*[C].txt"
        var optsInvalid = new MultiRenameOptions("Bad:Name*[C].txt");
        var plan = MultiRenamePlanner.Plan(items, optsInvalid, _fs);

        Assert.True(plan.HasErrors);
        Assert.All(plan.Items, i => Assert.Equal(MultiRenameStatus.InvalidName, i.Status));
        Assert.All(plan.Items, i => Assert.NotNull(i.ErrorMessage));
    }

    [Fact]
    public void Planner_DetectsBatchCollisions()
    {
        var items = new[]
        {
            new MultiRenameItemInput(@"C:\TestDir\alpha.txt", "alpha.txt", false, DateTime.UtcNow),
            new MultiRenameItemInput(@"C:\TestDir\beta.txt", "beta.txt", false, DateTime.UtcNow)
        };

        // Pattern renaming both to "common.txt" without counter
        var opts = new MultiRenameOptions("common.txt");
        var plan = MultiRenamePlanner.Plan(items, opts, _fs);

        Assert.True(plan.HasErrors);
        Assert.Equal(2, plan.ErrorCount);
        Assert.All(plan.Items, i => Assert.Equal(MultiRenameStatus.BatchCollision, i.Status));
    }

    [Fact]
    public void Planner_DetectsDiskCollisions_WhenTargetExistsAndNotPartOfBatch()
    {
        using (_fs.CreateFileForWrite(@"C:\TestDir\existing.txt")) { }

        var items = new[]
        {
            new MultiRenameItemInput(@"C:\TestDir\file1.txt", "file1.txt", false, DateTime.UtcNow)
        };

        var opts = new MultiRenameOptions("existing.txt");
        var plan = MultiRenamePlanner.Plan(items, opts, _fs);

        Assert.True(plan.HasErrors);
        Assert.Equal(MultiRenameStatus.DiskCollision, plan.Items[0].Status);
    }

    [Fact]
    public async Task Planner_AllowsIntraBatchSwaps_WithoutDiskCollision()
    {
        using (_fs.CreateFileForWrite(@"C:\TestDir\A.txt")) { }
        using (_fs.CreateFileForWrite(@"C:\TestDir\B.txt")) { }

        var items = new[]
        {
            new MultiRenameItemInput(@"C:\TestDir\A.txt", "A.txt", false, DateTime.UtcNow),
            new MultiRenameItemInput(@"C:\TestDir\B.txt", "B.txt", false, DateTime.UtcNow)
        };

        // Swap: A -> B, B -> A
        var customPlanItems = new List<MultiRenamePlanItem>
        {
            new(@"C:\TestDir\A.txt", "A.txt", "B.txt", @"C:\TestDir\B.txt", MultiRenameStatus.Valid),
            new(@"C:\TestDir\B.txt", "B.txt", "A.txt", @"C:\TestDir\A.txt", MultiRenameStatus.Valid)
        };
        var plan = new MultiRenamePlan(customPlanItems);

        // Execute two-phase operation
        var op = new MultiRenameOperation(_fs, plan);
        var res = await op.DoAsync();

        Assert.True(res.Success);
        Assert.True(_fs.FileExists(@"C:\TestDir\A.txt"));
        Assert.True(_fs.FileExists(@"C:\TestDir\B.txt"));

        // Undo
        var undoRes = await op.UndoAsync();
        Assert.True(undoRes.Success);
    }

    [Fact]
    public async Task Operation_TwoPhaseRename_ExecutesAndUndoesSuccessfully()
    {
        string file1 = @"C:\TestDir\doc1.txt";
        string file2 = @"C:\TestDir\doc2.txt";

        using (_fs.CreateFileForWrite(file1)) { }
        using (_fs.CreateFileForWrite(file2)) { }

        var items = new[]
        {
            new MultiRenameItemInput(file1, "doc1.txt", false, DateTime.UtcNow),
            new MultiRenameItemInput(file2, "doc2.txt", false, DateTime.UtcNow)
        };

        var opts = new MultiRenameOptions("Report_[C:01].[E]");
        var plan = MultiRenamePlanner.Plan(items, opts, _fs);

        Assert.False(plan.HasErrors);
        Assert.Equal(2, plan.TotalChanged);

        var op = new MultiRenameOperation(_fs, plan);
        var doRes = await op.DoAsync();

        Assert.True(doRes.Success);
        Assert.False(_fs.FileExists(file1));
        Assert.False(_fs.FileExists(file2));
        Assert.True(_fs.FileExists(@"C:\TestDir\Report_01.txt"));
        Assert.True(_fs.FileExists(@"C:\TestDir\Report_02.txt"));

        // Single-step compound undo
        var undoRes = await op.UndoAsync();
        Assert.True(undoRes.Success);
        Assert.True(_fs.FileExists(file1));
        Assert.True(_fs.FileExists(file2));
        Assert.False(_fs.FileExists(@"C:\TestDir\Report_01.txt"));
        Assert.False(_fs.FileExists(@"C:\TestDir\Report_02.txt"));
    }
}
