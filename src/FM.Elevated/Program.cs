using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FM.Core.Elevation;

namespace FM.Elevated;

public static class Program
{
    public static int Main(string[] args)
    {
        string? payloadPath = null;
        string? expectedHash = null;
        string? outPath = null;
        bool allowUntrustedPath = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--payload", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                payloadPath = args[++i];
            }
            else if (string.Equals(args[i], "--hash", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                expectedHash = args[++i];
            }
            else if (string.Equals(args[i], "--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                outPath = args[++i];
            }
            else if (string.Equals(args[i], "--allow-untrusted-elevation-path", StringComparison.OrdinalIgnoreCase))
            {
                allowUntrustedPath = true;
            }
        }

        if (string.IsNullOrWhiteSpace(payloadPath) ||
            string.IsNullOrWhiteSpace(expectedHash) ||
            string.IsNullOrWhiteSpace(outPath))
        {
            Console.Error.WriteLine("Error: Missing required arguments: --payload <path> --hash <hex> --out <path>");
            return ElevationExitCodes.InvalidArguments;
        }

        // Environment override for development
        if (Environment.GetEnvironmentVariable("FM_DEV_ALLOW_ELEVATION") == "1")
        {
            allowUntrustedPath = true;
        }

        // Validate binary execution location unless developer override is present
        if (!allowUntrustedPath)
        {
            string? procPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(procPath))
            {
                string? dir = Path.GetDirectoryName(procPath);
                string userDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(userDir) &&
                    dir.StartsWith(userDir, StringComparison.OrdinalIgnoreCase))
                {
                    WriteErrorResponse(outPath, "Worker cannot execute from a user-writable directory without developer override.");
                    return ElevationExitCodes.SecurityViolation;
                }
            }
        }

        if (!File.Exists(payloadPath))
        {
            WriteErrorResponse(outPath, $"Payload file not found: {payloadPath}");
            return ElevationExitCodes.InvalidArguments;
        }

        byte[] payloadBytes;
        try
        {
            using (var fs = new FileStream(payloadPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                payloadBytes = new byte[fs.Length];
                fs.ReadExactly(payloadBytes);
            }

            try
            {
                File.Delete(payloadPath);
            }
            catch { }
        }
        catch (Exception ex)
        {
            WriteErrorResponse(outPath, $"Failed to read payload file: {ex.Message}");
            return ElevationExitCodes.InvalidArguments;
        }

        // Verify SHA-256 hash
        string actualHash = Convert.ToHexString(SHA256.HashData(payloadBytes));
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            WriteErrorResponse(outPath, $"Cryptographic hash verification failed. Expected: {expectedHash}, Actual: {actualHash}");
            return ElevationExitCodes.HashMismatch;
        }

        ElevationPayload? payload;
        try
        {
            string json = Encoding.UTF8.GetString(payloadBytes);
            payload = JsonSerializer.Deserialize<ElevationPayload>(json);
            if (payload == null)
            {
                WriteErrorResponse(outPath, "Payload was empty or null.");
                return ElevationExitCodes.PayloadCorrupt;
            }
        }
        catch (Exception ex)
        {
            WriteErrorResponse(outPath, $"Failed to deserialize payload: {ex.Message}");
            return ElevationExitCodes.PayloadCorrupt;
        }

        var results = new List<ElevationItemResult>();
        bool anyFailed = false;

        foreach (var item in payload.Items)
        {
            if (!ElevatedPathValidator.IsPathSafeForElevation(item.SourcePath, out var reason))
            {
                results.Add(new ElevationItemResult(item.SourcePath, false, $"Security Denied: {reason}"));
                anyFailed = true;
                continue;
            }

            if (!string.IsNullOrEmpty(item.DestinationPath) &&
                !ElevatedPathValidator.IsPathSafeForElevation(item.DestinationPath, out var destReason))
            {
                results.Add(new ElevationItemResult(item.SourcePath, false, $"Security Denied for Destination: {destReason}"));
                anyFailed = true;
                continue;
            }

            try
            {
                ExecuteItem(item);
                results.Add(new ElevationItemResult(item.SourcePath, true));
            }
            catch (Exception ex)
            {
                results.Add(new ElevationItemResult(item.SourcePath, false, ex.Message));
                anyFailed = true;
            }
        }

        var response = new ElevationResponse(payload.OperationId, !anyFailed, results);
        try
        {
            string outJson = JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
            string? outDir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
            File.WriteAllText(outPath, outJson, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to write response file: {ex.Message}");
            return ElevationExitCodes.ExecutionFailed;
        }

        return anyFailed ? ElevationExitCodes.ExecutionFailed : ElevationExitCodes.Success;
    }

    private static void ExecuteItem(ElevationItem item)
    {
        switch (item.Kind)
        {
            case ElevationKind.CreateFolder:
                Directory.CreateDirectory(item.SourcePath);
                break;

            case ElevationKind.CreateFile:
                string? dir = Path.GetDirectoryName(item.SourcePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                byte[] bytes = string.IsNullOrEmpty(item.ContentBase64)
                    ? Array.Empty<byte>()
                    : Convert.FromBase64String(item.ContentBase64);
                File.WriteAllBytes(item.SourcePath, bytes);
                break;

            case ElevationKind.Rename:
                if (string.IsNullOrEmpty(item.DestinationPath))
                    throw new ArgumentException("Destination path is required for Rename.");

                if (File.Exists(item.SourcePath))
                {
                    File.Move(item.SourcePath, item.DestinationPath, overwrite: false);
                }
                else if (Directory.Exists(item.SourcePath))
                {
                    Directory.Move(item.SourcePath, item.DestinationPath);
                }
                else
                {
                    throw new FileNotFoundException($"Source item not found: {item.SourcePath}");
                }
                break;

            case ElevationKind.Delete:
                if (File.Exists(item.SourcePath))
                {
                    File.SetAttributes(item.SourcePath, FileAttributes.Normal);
                    File.Delete(item.SourcePath);
                }
                else if (Directory.Exists(item.SourcePath))
                {
                    if (ElevatedPathValidator.IsReparsePoint(item.SourcePath))
                    {
                        Directory.Delete(item.SourcePath, recursive: false);
                    }
                    else
                    {
                        Directory.Delete(item.SourcePath, recursive: true);
                    }
                }
                break;

            case ElevationKind.Copy:
                if (string.IsNullOrEmpty(item.DestinationPath))
                    throw new ArgumentException("Destination path is required for Copy.");

                if (File.Exists(item.SourcePath))
                {
                    string destDir = Path.GetDirectoryName(item.DestinationPath) ?? "";
                    if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                    string tempPart = item.DestinationPath + "." + Guid.NewGuid().ToString("N") + ".fmpart";
                    File.Copy(item.SourcePath, tempPart, overwrite: true);
                    File.Move(tempPart, item.DestinationPath, overwrite: true);
                }
                else if (Directory.Exists(item.SourcePath))
                {
                    CopyDirectoryRecursive(item.SourcePath, item.DestinationPath);
                }
                else
                {
                    throw new FileNotFoundException($"Source item not found: {item.SourcePath}");
                }
                break;

            case ElevationKind.Move:
                if (string.IsNullOrEmpty(item.DestinationPath))
                    throw new ArgumentException("Destination path is required for Move.");

                if (File.Exists(item.SourcePath))
                {
                    string destDir = Path.GetDirectoryName(item.DestinationPath) ?? "";
                    if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                    File.Move(item.SourcePath, item.DestinationPath, overwrite: true);
                }
                else if (Directory.Exists(item.SourcePath))
                {
                    Directory.Move(item.SourcePath, item.DestinationPath);
                }
                else
                {
                    throw new FileNotFoundException($"Source item not found: {item.SourcePath}");
                }
                break;

            default:
                throw new NotSupportedException($"ElevationKind '{item.Kind}' is not supported.");
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string targetFile = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, targetFile, overwrite: true);
        }

        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            if (ElevatedPathValidator.IsReparsePoint(subDir)) continue;
            string targetSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
            CopyDirectoryRecursive(subDir, targetSubDir);
        }
    }

    private static void WriteErrorResponse(string outPath, string message)
    {
        try
        {
            var response = new ElevationResponse("unknown", false, new List<ElevationItemResult>(), message);
            string json = JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
            string? dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(outPath, json, Encoding.UTF8);
        }
        catch { }
    }
}
