using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security;

namespace FM.Core.Operations.Zip;

/// <summary>
/// Pre-inspection and safety validation for ZIP archives prior to extraction.
/// </summary>
public static class ZipInspector
{
    private static readonly FieldInfo? BitFlagField = typeof(ZipArchiveEntry)
        .GetField("_generalPurposeBitFlag", BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>
    /// Checks whether an individual <see cref="ZipArchiveEntry"/> has the encryption bit set
    /// in its general purpose bit flag (Bit 0).
    /// </summary>
    public static bool IsEncrypted(ZipArchiveEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (BitFlagField != null)
        {
            try
            {
                var val = BitFlagField.GetValue(entry);
                if (val != null)
                {
                    ushort flag = Convert.ToUInt16(val);
                    if ((flag & 1) != 0)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Fallback to non-reflection behavior
            }
        }

        return false;
    }

    /// <summary>
    /// Inspects an opened <see cref="ZipArchive"/> against security limits, encryption,
    /// and Zip-Slip vulnerabilities. Returns total uncompressed bytes and file count.
    /// Throws descriptive exceptions on violation.
    /// </summary>
    public static (long TotalBytes, int FileCount) InspectArchive(ZipArchive archive, string destinationDirectory)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        if (archive.Entries.Count > ZipSecurityLimits.MaxEntries)
        {
            throw new InvalidOperationException(
                $"Archive contains {archive.Entries.Count:N0} entries, exceeding the security limit of {ZipSecurityLimits.MaxEntries:N0}.");
        }

        long totalBytes = 0;
        int fileCount = 0;

        foreach (var entry in archive.Entries)
        {
            // 1. Encryption check
            if (IsEncrypted(entry))
            {
                throw new InvalidOperationException(
                    "This archive contains password-protected / encrypted entries, which are not supported for extraction.");
            }

            bool isDirectory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\') || string.IsNullOrEmpty(entry.Name);

            // 2. Zip-Slip check
            ZipPathSanitizer.GetSafeDestinationPath(destinationDirectory, entry.FullName);

            if (!isDirectory)
            {
                fileCount++;

                // 3. Compression ratio check (zip bomb defense)
                if (entry.Length > ZipSecurityLimits.RatioCheckThresholdBytes && entry.CompressedLength > 0)
                {
                    double ratio = (double)entry.Length / entry.CompressedLength;
                    if (ratio > ZipSecurityLimits.MaxCompressionRatio)
                    {
                        throw new InvalidOperationException(
                            $"Archive entry '{entry.FullName}' has a compression ratio of {ratio:F1}:1, exceeding the safety threshold of {ZipSecurityLimits.MaxCompressionRatio}:1 (potential zip bomb).");
                    }
                }

                totalBytes += entry.Length;
                if (totalBytes > ZipSecurityLimits.MaxTotalUncompressedBytes)
                {
                    throw new InvalidOperationException(
                        $"Archive total uncompressed size ({totalBytes / (1024 * 1024):N0} MB) exceeds maximum allowed limit of {ZipSecurityLimits.MaxTotalUncompressedBytes / (1024 * 1024 * 1024)} GB.");
                }
            }
        }

        return (totalBytes, fileCount);
    }
}
