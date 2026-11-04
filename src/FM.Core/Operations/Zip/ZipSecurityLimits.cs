namespace FM.Core.Operations.Zip;

/// <summary>
/// Security limits and thresholds for zip extraction to defend against
/// zip-bomb (decompression bomb) and denial of service attacks.
/// </summary>
public static class ZipSecurityLimits
{
    /// <summary>
    /// Maximum number of entries permitted in a single archive.
    /// Default: 100,000 entries.
    /// </summary>
    public const int MaxEntries = 100_000;

    /// <summary>
    /// Maximum cumulative uncompressed byte size across all entries in an archive.
    /// Default: 25 GB.
    /// </summary>
    public const long MaxTotalUncompressedBytes = 25L * 1024 * 1024 * 1024;

    /// <summary>
    /// Maximum allowed compression ratio (uncompressed size / compressed size)
    /// for files exceeding <see cref="RatioCheckThresholdBytes"/>.
    /// Default: 200:1.
    /// </summary>
    public const double MaxCompressionRatio = 200.0;

    /// <summary>
    /// File size threshold (in uncompressed bytes) below which the compression ratio check is waived.
    /// Tiny files (e.g., small text) frequently exhibit harmless high ratios.
    /// Default: 10 MB.
    /// </summary>
    public const long RatioCheckThresholdBytes = 10L * 1024 * 1024;
}

