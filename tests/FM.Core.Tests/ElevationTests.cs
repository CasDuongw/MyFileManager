using System.Text.Json;
using FM.Core.Elevation;

namespace FM.Core.Tests;

public class ElevationTests
{
    [Fact]
    public void ElevatedPathValidator_EmptyOrNull_ReturnsFalse()
    {
        Assert.False(ElevatedPathValidator.IsPathSafeForElevation("", out var reason1));
        Assert.NotNull(reason1);

        Assert.False(ElevatedPathValidator.IsPathSafeForElevation("   ", out var reason2));
        Assert.NotNull(reason2);
    }

    [Fact]
    public void ElevatedPathValidator_DriveRoot_IsRejected()
    {
        Assert.False(ElevatedPathValidator.IsPathSafeForElevation(@"C:\", out var reasonC));
        Assert.Contains("gốc", reasonC ?? "");

        Assert.False(ElevatedPathValidator.IsPathSafeForElevation(@"D:\", out var reasonD));
        Assert.Contains("gốc", reasonD ?? "");
    }

    [Fact]
    public void ElevatedPathValidator_WindowsDirectory_IsRejected()
    {
        string? winDir = Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows";
        Assert.False(ElevatedPathValidator.IsPathSafeForElevation(winDir, out var reasonRoot));
        Assert.NotNull(reasonRoot);

        string system32 = Path.Combine(winDir, "System32");
        Assert.False(ElevatedPathValidator.IsPathSafeForElevation(system32, out var reasonSub));
        Assert.NotNull(reasonSub);
    }

    [Fact]
    public void ElevatedPathValidator_StandardSubfolder_IsAllowed()
    {
        string samplePath = @"C:\TestDirectory\SubFolder\File.txt";
        bool isSafe = ElevatedPathValidator.IsPathSafeForElevation(samplePath, out var reason);
        Assert.True(isSafe);
        Assert.Null(reason);
    }

    [Fact]
    public void ElevationPayload_SerializeAndDeserialize_RoundTripsCorrectly()
    {
        var items = new List<ElevationItem>
        {
            new(ElevationKind.CreateFolder, @"C:\TestDir\NewFolder"),
            new(ElevationKind.CreateFile, @"C:\TestDir\file.txt", ContentBase64: Convert.ToBase64String("Hello"u8.ToArray())),
            new(ElevationKind.Rename, @"C:\TestDir\old.txt", @"C:\TestDir\new.txt"),
            new(ElevationKind.Delete, @"C:\TestDir\unwanted.txt")
        };

        var payload = new ElevationPayload("op-12345", items);

        string json = JsonSerializer.Serialize(payload);
        var deserialized = JsonSerializer.Deserialize<ElevationPayload>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("op-12345", deserialized.OperationId);
        Assert.Equal(4, deserialized.Items.Count);
        Assert.Equal(ElevationKind.CreateFolder, deserialized.Items[0].Kind);
        Assert.Equal(ElevationKind.CreateFile, deserialized.Items[1].Kind);
        Assert.Equal(ElevationKind.Rename, deserialized.Items[2].Kind);
        Assert.Equal(ElevationKind.Delete, deserialized.Items[3].Kind);
    }

    [Fact]
    public void ElevationResponse_SerializeAndDeserialize_RoundTripsCorrectly()
    {
        var results = new List<ElevationItemResult>
        {
            new(@"C:\TestDir\file1.txt", true),
            new(@"C:\TestDir\file2.txt", false, "Permission denied")
        };

        var response = new ElevationResponse("op-999", false, results, "One or more operations failed");

        string json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<ElevationResponse>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("op-999", deserialized.OperationId);
        Assert.False(deserialized.AllSucceeded);
        Assert.Equal(2, deserialized.Results.Count);
        Assert.True(deserialized.Results[0].Success);
        Assert.False(deserialized.Results[1].Success);
        Assert.Equal("Permission denied", deserialized.Results[1].ErrorMessage);
    }

    [Fact]
    public void ElevationExitCodes_ValuesAreExpected()
    {
        Assert.Equal(0, ElevationExitCodes.Success);
        Assert.Equal(1, ElevationExitCodes.InvalidArguments);
        Assert.Equal(2, ElevationExitCodes.SecurityViolation);
        Assert.Equal(10, ElevationExitCodes.HashMismatch);
        Assert.Equal(11, ElevationExitCodes.PayloadCorrupt);
        Assert.Equal(12, ElevationExitCodes.ExecutionFailed);
    }
}
