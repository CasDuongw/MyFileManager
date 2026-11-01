using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FM.Core.Search;
using Xunit;

namespace FM.Core.Tests;

public class PreparedMatcherTests
{
    private record TestFolder(string FullPath, string Name, string NormName, ulong Mask, string? ParentPath, string? NormParentPath)
    {
        public static TestFolder FromPath(string fullPath)
        {
            string name = Path.GetFileName(fullPath);
            if (string.IsNullOrEmpty(name)) name = fullPath;
            string normName = NameMatcher.Normalize(name);
            ulong mask = CharacterBitmask.Compute(normName);
            string? parent = Path.GetDirectoryName(fullPath);
            string? normParent = parent != null ? NameMatcher.Normalize(parent) : null;
            return new TestFolder(fullPath, name, normName, mask, parent, normParent);
        }
    }

    private readonly List<TestFolder> _dataset = new()
    {
        TestFolder.FromPath(@"C:\Users\u\Documents"),
        TestFolder.FromPath(@"C:\Users\u\Downloads"),
        TestFolder.FromPath(@"C:\Users\u\Desktop"),
        TestFolder.FromPath(@"D:\Dev\MyFileManager"),
        TestFolder.FromPath(@"D:\Dev\Projects\web"),
        TestFolder.FromPath(@"D:\Dev\Projects\web-api"),
        TestFolder.FromPath(@"D:\Dev\Projects\Web Design"),
        TestFolder.FromPath(@"E:\Học tập\Đồ án"),
        TestFolder.FromPath(@"E:\Học tập\Đại học"),
        TestFolder.FromPath(@"D:\Dev\Projects\web\node_modules\deep_noise_package")
    };

    private List<string> MatchPaths(string query)
    {
        var pq = PreparedQuery.Create(query);
        var results = PreparedMatcher.SelectTopK(
            _dataset,
            pq,
            10,
            f => (f.Name, f.NormName, f.Mask, f.ParentPath, f.NormParentPath));

        return results.Select(r => r.Item.FullPath).ToList();
    }

    [Fact]
    public void Query_Doc_Returns_Documents_First()
    {
        var results = MatchPaths("doc");
        Assert.NotEmpty(results);
        Assert.Equal(@"C:\Users\u\Documents", results[0]);
    }

    [Fact]
    public void Query_Down_Returns_Downloads_First()
    {
        var results = MatchPaths("down");
        Assert.NotEmpty(results);
        Assert.Equal(@"C:\Users\u\Downloads", results[0]);
    }

    [Fact]
    public void Query_Mfm_Returns_MyFileManager_First()
    {
        var results = MatchPaths("mfm");
        Assert.NotEmpty(results);
        Assert.Equal(@"D:\Dev\MyFileManager", results[0]);
    }

    [Fact]
    public void Query_ProjWeb_Returns_ProjectsWeb_Before_Other_Webs()
    {
        var results = MatchPaths("proj web");
        Assert.True(results.Count >= 3);

        int idxExact = results.IndexOf(@"D:\Dev\Projects\web");
        int idxApi = results.IndexOf(@"D:\Dev\Projects\web-api");
        int idxDesign = results.IndexOf(@"D:\Dev\Projects\Web Design");

        Assert.True(idxExact >= 0, "Projects\\web must be in results");
        Assert.True(idxApi >= 0, "Projects\\web-api must be in results");
        Assert.True(idxDesign >= 0, "Projects\\Web Design must be in results");

        Assert.True(idxExact < idxApi, "Projects\\web must rank before web-api");
        Assert.True(idxExact < idxDesign, "Projects\\web must rank before Web Design");
    }

    [Fact]
    public void Query_Web_ExactMatch_RanksBefore_Prefixes()
    {
        var results = MatchPaths("web");
        Assert.True(results.Count >= 2);
        Assert.Equal(@"D:\Dev\Projects\web", results[0]);
    }

    [Theory]
    [InlineData("do an")]
    [InlineData("đo an")]
    public void Query_DoAn_Matches_DoAn(string query)
    {
        var results = MatchPaths(query);
        Assert.NotEmpty(results);
        Assert.Equal(@"E:\Học tập\Đồ án", results[0]);
    }

    [Fact]
    public void Query_DaiHoc_Matches_DaiHoc()
    {
        var results = MatchPaths("dai hoc");
        Assert.NotEmpty(results);
        Assert.Equal(@"E:\Học tập\Đại học", results[0]);
    }

    [Fact]
    public void Query_HocTap_Matches_Children()
    {
        var results = MatchPaths("hoc tap");
        Assert.True(results.Count >= 2);
        Assert.Contains(@"E:\Học tập\Đồ án", results);
        Assert.Contains(@"E:\Học tập\Đại học", results);
    }

    [Fact]
    public void Query_Dwn_Matches_Downloads_Not_Unrelated_Paths()
    {
        var results = MatchPaths("dwn");
        Assert.NotEmpty(results);
        Assert.Equal(@"C:\Users\u\Downloads", results[0]);
        Assert.DoesNotContain(@"C:\Users\u\Documents", results);
    }

    [Fact]
    public void Query_NoMatch_ReturnsEmptyList_NeverThrows()
    {
        var results = MatchPaths("zzzznonexistent9999");
        Assert.Empty(results);
    }

    [Fact]
    public void PathSegmentQuery_ProjSlashWeb_Matches_InOrder()
    {
        var results = MatchPaths(@"proj/web");
        Assert.NotEmpty(results);
        Assert.Equal(@"D:\Dev\Projects\web", results[0]);
    }

    [Fact]
    public void BitmaskPrefilter_PropertyTest_NeverRejects_TrueSubsequence()
    {
        var rng = new Random(42);
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789_- ";

        for (int iter = 0; iter < 1000; iter++)
        {
            // Generate random candidate
            int cLen = rng.Next(5, 30);
            char[] candChars = new char[cLen];
            for (int i = 0; i < cLen; i++)
            {
                candChars[i] = alphabet[rng.Next(alphabet.Length)];
            }
            string candidate = new string(candChars);
            string normCandidate = NameMatcher.Normalize(candidate);
            ulong candidateMask = CharacterBitmask.Compute(normCandidate);

            // Extract a random valid subsequence
            int qLen = rng.Next(1, Math.Min(8, normCandidate.Length + 1));
            var subSeq = new List<char>();
            int lastIdx = 0;
            for (int q = 0; q < qLen && lastIdx < normCandidate.Length; q++)
            {
                int nextIdx = rng.Next(lastIdx, normCandidate.Length);
                subSeq.Add(normCandidate[nextIdx]);
                lastIdx = nextIdx + 1;
            }
            string query = new string(subSeq.ToArray());
            string normQuery = NameMatcher.Normalize(query);
            ulong queryMask = CharacterBitmask.Compute(normQuery);

            bool isSubset = CharacterBitmask.IsSubset(queryMask, candidateMask);
            Assert.True(isSubset, $"Bitmask prefilter produced false negative for query '{normQuery}' in candidate '{normCandidate}'");
        }
    }

    [Fact]
    public void TopK_MatchesBruteForceReference()
    {
        var rng = new Random(1337);
        var items = new List<TestFolder>(500);
        for (int i = 0; i < 500; i++)
        {
            string name = $"Folder_{i:D4}_{(char)('a' + (i % 26))}_test";
            items.Add(TestFolder.FromPath($@"C:\Test\{name}"));
        }

        var query = PreparedQuery.Create("folder a");

        // Brute force with deterministic tie-breaking (score descending, shorter path, alphabetical)
        var bruteForce = items
            .Select(f => new
            {
                Item = f,
                Score = PreparedMatcher.Match(query, f.Name, f.NormName, f.Mask, f.ParentPath, f.NormParentPath)
            })
            .Where(x => x.Score >= 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Item.FullPath.Length)
            .ThenBy(x => x.Item.FullPath, StringComparer.OrdinalIgnoreCase)
            .Take(15)
            .Select(x => x.Item.FullPath)
            .ToList();

        // Top-K selection
        var topK = PreparedMatcher.SelectTopK(
            items,
            query,
            15,
            f => (f.Name, f.NormName, f.Mask, f.ParentPath, f.NormParentPath))
            .Select(x => x.Item.FullPath)
            .ToList();

        Assert.Equal(bruteForce.Count, topK.Count);
        for (int i = 0; i < bruteForce.Count; i++)
        {
            Assert.Equal(bruteForce[i], topK[i]);
        }
    }
}
