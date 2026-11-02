using FM.Core.FileSystem;
using FM.Core.PathResolution;

namespace FM.Core.CommandLine;

/// <summary>
/// Abstraction for parsing command line arguments in a testable, WPF-free manner.
/// </summary>
public interface ICommandLineParser
{
    CommandLineParsedResult Parse(string[]? args, IFileSystem fileSystem, IPathResolver? pathResolver = null);
}

