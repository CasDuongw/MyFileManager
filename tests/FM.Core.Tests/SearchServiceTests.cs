using FM.Core.FileSystem;
using FM.Core.Search;
using Xunit;

namespace FM.Core.Tests;

public class SearchServiceTests
{
    private readonly InMemoryFileSystem _fileSystem;
    private readonly SearchService _searchService;

    public SearchServiceTests()
    {
        _fileSystem = new InMemoryFileSystem();
        _searchService = new SearchService(_fileSystem);
    }

    [Fact]
    public async Task Search_Recursion_FindsDescendantMatches()
    {
        _fileSystem.CreateDirectory(@"C:\Project");
        _fileSystem.CreateDirectory(@"C:\Project\SubA");
        _fileSystem.CreateDirectory(@"C:\Project\SubA\Deep");
        _fileSystem.CreateDirectory(@"C:\Project\SubB");

        _fileSystem.CreateFile(@"C:\Project\file1.txt");
        _fileSystem.CreateFile(@"C:\Project\SubA\file2.txt");
        _fileSystem.CreateFile(@"C:\Project\SubA\Deep\file3.txt");
        _fileSystem.CreateFile(@"C:\Project\SubB\other.pdf");

        var query = new SearchQuery(
            QueryText: "file",
            Scope: SearchScope.CurrentFolder,
            RootPaths: new[] { @"C:\Project" });

        var results = new List<SearchResult>();
        await foreach (var batch in _searchService.SearchAsync(query, CancellationToken.None))
        {
            results.AddRange(batch);
        }

        Assert.Equal(3, results.Count);
        Assert.Contains(results, r => r.Entry.Name == "file1.txt" && r.ParentDirectory == @"C:\Project");
        Assert.Contains(results, r => r.Entry.Name == "file2.txt" && r.ParentDirectory == @"C:\Project\SubA");
        Assert.Contains(results, r => r.Entry.Name == "file3.txt" && r.ParentDirectory == @"C:\Project\SubA\Deep");
        Assert.False(_searchService.IsResultsLimited);
        Assert.Equal(0, _searchService.SkippedFolderCount);
    }

    [Fact]
    public void DeduplicateRoots_RemovesNestedPaths()
    {
        var input = new[]
        {
            @"C:\Project",
            @"C:\Project\SubA",
            @"C:\Project\SubA\Deep",
            @"C:\AnotherFolder"
        };

        var deduplicated = SearchService.DeduplicateRoots(input);

        Assert.Equal(2, deduplicated.Count);
        Assert.Contains(@"C:\Project", deduplicated);
        Assert.Contains(@"C:\AnotherFolder", deduplicated);
    }

    [Fact]
    public async Task Search_ReparsePoints_SkippedToAvoidCycles()
    {
        _fileSystem.CreateDirectory(@"C:\Project");
        _fileSystem.CreateDirectory(@"C:\Project\RealFolder");
        _fileSystem.CreateFile(@"C:\Project\RealFolder\valid.txt");

        // Create reparse point folder
        _fileSystem.CreateReparsePoint(@"C:\Project\JunctionFolder", isDirectory: true);
        _fileSystem.CreateFile(@"C:\Project\JunctionFolder\nested_in_junction.txt");

        var query = new SearchQuery(
            QueryText: "txt",
            Scope: SearchScope.CurrentFolder,
            RootPaths: new[] { @"C:\Project" });

        var results = new List<SearchResult>();
        await foreach (var batch in _searchService.SearchAsync(query, CancellationToken.None))
        {
            results.AddRange(batch);
        }

        Assert.Single(results);
        Assert.Equal("valid.txt", results[0].Entry.Name);
        Assert.DoesNotContain(results, r => r.Entry.Name == "nested_in_junction.txt");
    }

    [Fact]
    public async Task Search_InaccessibleDirectories_IncrementsSkippedFolderCount()
    {
        _fileSystem.CreateDirectory(@"C:\Project");
        _fileSystem.CreateDirectory(@"C:\Project\Public");
        _fileSystem.CreateDirectory(@"C:\Project\Restricted");
        _fileSystem.CreateFile(@"C:\Project\Public\doc.txt");

        // Inject access denied on Restricted folder
        _fileSystem.InjectEnumerationFailure(@"C:\Project\Restricted", new UnauthorizedAccessException("Access denied"));

        var query = new SearchQuery(
            QueryText: "doc",
            Scope: SearchScope.CurrentFolder,
            RootPaths: new[] { @"C:\Project" });

        var results = new List<SearchResult>();
        await foreach (var batch in _searchService.SearchAsync(query, CancellationToken.None))
        {
            results.AddRange(batch);
        }

        Assert.Single(results);
        Assert.Equal("doc.txt", results[0].Entry.Name);
        Assert.True(_searchService.SkippedFolderCount >= 1);
    }

    [Fact]
    public async Task Search_MaxResultsCap_HaltsEnumeration()
    {
        _fileSystem.CreateDirectory(@"C:\Project");
        for (int i = 0; i < 50; i++)
        {
            _fileSystem.CreateFile(@$"C:\Project\test_{i:D2}.dat");
        }

        var query = new SearchQuery(
            QueryText: "test",
            Scope: SearchScope.CurrentFolder,
            RootPaths: new[] { @"C:\Project" },
            MaxResults: 15);

        var results = new List<SearchResult>();
        await foreach (var batch in _searchService.SearchAsync(query, CancellationToken.None))
        {
            results.AddRange(batch);
        }

        Assert.Equal(15, results.Count);
        Assert.True(_searchService.IsResultsLimited);
    }

    [Fact]
    public async Task Search_Cancellation_HaltsSearchPromptly()
    {
        _fileSystem.CreateDirectory(@"C:\Project");
        for (int i = 0; i < 50; i++)
        {
            _fileSystem.CreateFile(@$"C:\Project\item_{i:D2}.dat");
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-canceled

        var query = new SearchQuery(
            QueryText: "item",
            Scope: SearchScope.CurrentFolder,
            RootPaths: new[] { @"C:\Project" });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var batch in _searchService.SearchAsync(query, cts.Token))
            {
                // Should not yield or cancel
            }
        });
    }

    [Fact]
    public async Task Search_FirstBatchFast_YieldsSmallInitialBatch()
    {
        _fileSystem.CreateDirectory(@"C:\Project");
        for (int i = 0; i < 60; i++)
        {
            _fileSystem.CreateFile(@$"C:\Project\match_{i:D2}.dat");
        }

        var query = new SearchQuery(
            QueryText: "match",
            Scope: SearchScope.CurrentFolder,
            RootPaths: new[] { @"C:\Project" });

        IReadOnlyList<SearchResult>? firstBatch = null;
        await foreach (var batch in _searchService.SearchAsync(query, CancellationToken.None))
        {
            firstBatch = batch;
            break;
        }

        Assert.NotNull(firstBatch);
        Assert.Equal(25, firstBatch.Count); // First batch size is 25
    }
}
