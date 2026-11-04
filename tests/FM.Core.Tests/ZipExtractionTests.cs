using System.IO;
using System.IO.Compression;
using System.Security;
using FM.Core.FileSystem;
using FM.Core.Operations.Transfer;
using FM.Core.Operations.Zip;

namespace FM.Core.Tests;

public class ZipExtractionTests
{
    private static byte[] CreateZipBytes(params (string EntryName, byte[] Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var entryStream = entry.Open();
                entryStream.Write(content, 0, content.Length);
            }
        }
        return ms.ToArray();
    }

    private static byte[] CreateEncryptedFlagZipBytes(string entryName, byte[] content)
    {
        byte[] bytes = CreateZipBytes((entryName, content));

        // Find central directory header 0x02014b50 and flip bit 0 of general purpose flag (offset 8)
        byte[] cdSig = new byte[] { 0x50, 0x4b, 0x01, 0x02 };
        for (int i = 0; i < bytes.Length - 10; i++)
        {
            if (bytes[i] == cdSig[0] && bytes[i + 1] == cdSig[1] && bytes[i + 2] == cdSig[2] && bytes[i + 3] == cdSig[3])
            {
                bytes[i + 8] |= 1; // Bit 0 = encrypted
            }
        }

        // Find local file header 0x04034b50 and flip bit 0 (offset 6)
        byte[] localSig = new byte[] { 0x50, 0x4b, 0x03, 0x04 };
        for (int i = 0; i < bytes.Length - 10; i++)
        {
            if (bytes[i] == localSig[0] && bytes[i + 1] == localSig[1] && bytes[i + 2] == localSig[2] && bytes[i + 3] == localSig[3])
            {
                bytes[i + 6] |= 1; // Bit 0 = encrypted
            }
        }

        return bytes;
    }

    [Fact]
    public void ZipPathSanitizer_SanitizesReservedNamesAndInvalidChars()
    {
        Assert.Equal("_con.txt", ZipPathSanitizer.SanitizeSegment("con.txt"));
        Assert.Equal("_aux", ZipPathSanitizer.SanitizeSegment("aux"));
        Assert.Equal("_com1.log", ZipPathSanitizer.SanitizeSegment("com1.log"));
        Assert.Equal("file_name_.txt", ZipPathSanitizer.SanitizeSegment("file*name?.txt"));
        Assert.Equal("trimmed", ZipPathSanitizer.SanitizeSegment("trimmed... "));

        string relPath = ZipPathSanitizer.SanitizeRelativePath("folder. /con.txt");
        Assert.Equal(@"folder\_con.txt", relPath);
    }

    [Fact]
    public void ZipPathSanitizer_ZipSlip_ThrowsSecurityException()
    {
        string dest = @"C:\destination\folder";

        // Path traversal with ../..
        Assert.Throws<SecurityException>(() =>
            ZipPathSanitizer.GetSafeDestinationPath(dest, "../../evil.txt"));

        // Path traversal with parent directory prefix
        Assert.Throws<SecurityException>(() =>
            ZipPathSanitizer.GetSafeDestinationPath(dest, @"..\..\Windows\System32\malicious.dll"));
    }

    [Fact]
    public async Task Extract_ValidZipArchive_ExtractsFilesAndDirectoriesCorrectly()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(@"C:\archives");
        fs.CreateDirectory(@"C:\extracted");

        byte[] textContent = "Hello from zip!"u8.ToArray();
        byte[] subTextContent = "Subfolder file content"u8.ToArray();

        byte[] zipBytes = CreateZipBytes(
            ("readme.txt", textContent),
            ("subfolder/data.bin", subTextContent));

        fs.CreateFile(@"C:\archives\test.zip", zipBytes);

        var engine = new TransferEngine(fs);
        var progressList = new List<TransferProgress>();
        var progress = new Progress<TransferProgress>(p => progressList.Add(p));

        var request = new TransferRequest(
            TransferKind.Extract,
            new[] { @"C:\archives\test.zip" },
            @"C:\extracted",
            new TransferOptions());

        var result = await engine.RunAsync(request, progress);

        Assert.Equal(TransferState.Completed, result.FinalState);
        Assert.True(fs.FileExists(@"C:\extracted\readme.txt"));
        Assert.True(fs.FileExists(@"C:\extracted\subfolder\data.bin"));

        // Verify content
        using var s1 = fs.OpenRead(@"C:\extracted\readme.txt");
        using var ms1 = new MemoryStream();
        s1.CopyTo(ms1);
        Assert.Equal(textContent, ms1.ToArray());

        using var s2 = fs.OpenRead(@"C:\extracted\subfolder\data.bin");
        using var ms2 = new MemoryStream();
        s2.CopyTo(ms2);
        Assert.Equal(subTextContent, ms2.ToArray());
    }

    [Fact]
    public async Task Extract_EncryptedZip_ThrowsClearErrorMessage()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(@"C:\archives");
        fs.CreateDirectory(@"C:\extracted");

        byte[] encZip = CreateEncryptedFlagZipBytes("secret.txt", "TopSecretContent"u8.ToArray());
        fs.CreateFile(@"C:\archives\locked.zip", encZip);

        var engine = new TransferEngine(fs);
        var request = new TransferRequest(
            TransferKind.Extract,
            new[] { @"C:\archives\locked.zip" },
            @"C:\extracted",
            new TransferOptions());

        var result = await engine.RunAsync(request);

        Assert.Equal(TransferState.Failed, result.FinalState);
        Assert.Contains("password-protected / encrypted", result.Items[0].Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Extract_InsufficientDiskSpace_PreflightFails()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(@"C:\archives");
        fs.CreateDirectory(@"C:\extracted");

        byte[] fileBytes = new byte[1024];
        byte[] zipBytes = CreateZipBytes(("file.bin", fileBytes));
        fs.CreateFile(@"C:\archives\test.zip", zipBytes);

        // Limit free space to 500 bytes (less than 1024 required)
        fs.SetVolumeFreeSpace(@"C:\", 500);

        var engine = new TransferEngine(fs);
        var request = new TransferRequest(
            TransferKind.Extract,
            new[] { @"C:\archives\test.zip" },
            @"C:\extracted",
            new TransferOptions());

        var result = await engine.RunAsync(request);

        Assert.Equal(TransferState.Failed, result.FinalState);
        Assert.Contains("Insufficient disk space", result.Items[0].Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(fs.FileExists(@"C:\extracted\file.bin"));
    }

    [Fact]
    public async Task Extract_ConflictHandling_KeepBothAndOverwrite()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(@"C:\archives");
        fs.CreateDirectory(@"C:\extracted");

        // Existing file in destination
        fs.CreateFile(@"C:\extracted\doc.txt", "Original"u8.ToArray());

        byte[] zipBytes = CreateZipBytes(("doc.txt", "ZipVersion"u8.ToArray()));
        fs.CreateFile(@"C:\archives\doc.zip", zipBytes);

        // 1. KeepBoth (default)
        var engine = new TransferEngine(fs);
        var requestKeepBoth = new TransferRequest(
            TransferKind.Extract,
            new[] { @"C:\archives\doc.zip" },
            @"C:\extracted",
            new TransferOptions(DefaultConflict: ConflictAction.KeepBoth));

        var result1 = await engine.RunAsync(requestKeepBoth);
        Assert.Equal(TransferState.Completed, result1.FinalState);
        Assert.True(fs.FileExists(@"C:\extracted\doc.txt"));
        Assert.True(fs.FileExists(@"C:\extracted\doc (2).txt"));

        // 2. Overwrite
        var requestOverwrite = new TransferRequest(
            TransferKind.Extract,
            new[] { @"C:\archives\doc.zip" },
            @"C:\extracted",
            new TransferOptions(DefaultConflict: ConflictAction.Overwrite));

        var result2 = await engine.RunAsync(requestOverwrite);
        Assert.Equal(TransferState.Completed, result2.FinalState);

        using var s = fs.OpenRead(@"C:\extracted\doc.txt");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        Assert.Equal("ZipVersion"u8.ToArray(), ms.ToArray());
    }

    [Fact]
    public async Task Extract_Undo_RestoresReplacedFilesAndDeletesExtractedFiles()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(@"C:\archives");
        fs.CreateDirectory(@"C:\extracted");
        fs.CreateDirectory(@"C:\staging");

        fs.CreateFile(@"C:\extracted\replaced.txt", "InitialDestinationData"u8.ToArray());

        byte[] zipBytes = CreateZipBytes(
            ("newfile.txt", "NewFileData"u8.ToArray()),
            ("replaced.txt", "OverwritingData"u8.ToArray()));
        fs.CreateFile(@"C:\archives\pack.zip", zipBytes);

        var stager = new StagingBackupStore(fs, @"C:\staging");
        var engine = new TransferEngine(fs, stager);

        var request = new TransferRequest(
            TransferKind.Extract,
            new[] { @"C:\archives\pack.zip" },
            @"C:\extracted",
            new TransferOptions(DefaultConflict: ConflictAction.Overwrite));

        var result = await engine.RunAsync(request);
        Assert.Equal(TransferState.Completed, result.FinalState);
        Assert.True(fs.FileExists(@"C:\extracted\newfile.txt"));

        // Create undo operation and undo
        var undoOp = new TransferUndoOperation(fs, TransferKind.Extract, result, stager, request.OperationId, engine: engine);
        Assert.True(undoOp.CanUndo);
        Assert.StartsWith("Extraction of", undoOp.Description);

        var undoResult = await undoOp.UndoAsync();
        Assert.True(undoResult.Success);

        // newfile.txt deleted, replaced.txt restored to original
        Assert.False(fs.FileExists(@"C:\extracted\newfile.txt"));
        Assert.True(fs.FileExists(@"C:\extracted\replaced.txt"));

        using var s = fs.OpenRead(@"C:\extracted\replaced.txt");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        Assert.Equal("InitialDestinationData"u8.ToArray(), ms.ToArray());
    }

    [Fact]
    public async Task Extract_Cancellation_DeletesFmPartAndCleansUp()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(@"C:\archives");
        fs.CreateDirectory(@"C:\extracted");

        byte[] largeData = new byte[500_000];
        byte[] zipBytes = CreateZipBytes(("bigfile.dat", largeData));
        fs.CreateFile(@"C:\archives\big.zip", zipBytes);

        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var engine = new TransferEngine(fs);
        var request = new TransferRequest(
            TransferKind.Extract,
            new[] { @"C:\archives\big.zip" },
            @"C:\extracted",
            new TransferOptions());

        var result = await engine.RunAsync(request, ct: cts.Token);

        Assert.Equal(TransferState.Cancelled, result.FinalState);
        Assert.False(fs.FileExists(@"C:\extracted\bigfile.dat"));
        Assert.False(fs.FileExists(@"C:\extracted\bigfile.dat.fmpart"));
    }
}
