namespace FM.Core.CommandLine;

/// <summary>
/// Represents a single target extracted from command-line arguments.
/// </summary>
public record CommandLineTarget(
    string TargetDirectory,
    string? SelectFileName = null,
    string OriginalArgument = "",
    bool IsValid = true,
    string? ErrorMessage = null);

