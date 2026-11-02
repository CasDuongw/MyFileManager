namespace FM.Core.CommandLine;

/// <summary>
/// Result of parsing command line arguments.
/// </summary>
public record CommandLineParsedResult(
    IReadOnlyList<CommandLineTarget> Targets)
{
    public static readonly CommandLineParsedResult Empty = new(Array.Empty<CommandLineTarget>());

    public bool HasTargets => Targets.Count > 0;
}

