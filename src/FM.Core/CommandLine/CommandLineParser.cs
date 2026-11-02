using System.IO;
using FM.Core.FileSystem;
using FM.Core.PathResolution;

namespace FM.Core.CommandLine;

/// <summary>
/// Production command line parser adhering to Windows Explorer conventions.
/// Handles:
/// - Single / multiple paths: MyFileManager.exe &lt;path1&gt; &lt;path2&gt;
/// - File paths: opens parent folder and flags the file for selection
/// - Explorer syntax: /select,&lt;path&gt; or /select, &lt;path&gt;
/// - Environment variables and shell shortcuts via IPathResolver
/// - Invalid / non-existent paths (produces non-crashing error target)
/// </summary>
public sealed class CommandLineParser : ICommandLineParser
{
    public static readonly CommandLineParser Instance = new();

    public CommandLineParsedResult Parse(string[]? args, IFileSystem fileSystem, IPathResolver? pathResolver = null)
    {
        if (args == null || args.Length == 0)
        {
            return CommandLineParsedResult.Empty;
        }

        pathResolver ??= PathResolver.Instance;
        var targets = new List<CommandLineTarget>();

        for (int i = 0; i < args.Length; i++)
        {
            string rawArg = args[i].Trim();
            if (string.IsNullOrWhiteSpace(rawArg)) continue;

            // 1. Check for /select, or -select, Explorer syntax
            bool isExplicitSelect = false;
            string candidatePath = rawArg;

            const string selectPrefixSlash = "/select,";
            const string selectPrefixDash = "-select,";

            if (rawArg.StartsWith(selectPrefixSlash, StringComparison.OrdinalIgnoreCase))
            {
                isExplicitSelect = true;
                candidatePath = rawArg[selectPrefixSlash.Length..].Trim();
            }
            else if (rawArg.StartsWith(selectPrefixDash, StringComparison.OrdinalIgnoreCase))
            {
                isExplicitSelect = true;
                candidatePath = rawArg[selectPrefixDash.Length..].Trim();
            }
            else if (string.Equals(rawArg, "/select", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(rawArg, "-select", StringComparison.OrdinalIgnoreCase))
            {
                // Format: /select <path>
                if (i + 1 < args.Length)
                {
                    isExplicitSelect = true;
                    i++;
                    candidatePath = args[i].Trim();
                }
            }

            // If "/select," was passed with next token as path (e.g. ["/select,", "C:\foo\bar.txt"])
            if (isExplicitSelect && string.IsNullOrEmpty(candidatePath) && i + 1 < args.Length)
            {
                i++;
                candidatePath = args[i].Trim();
            }

            // Strip surrounding quotes if present
            candidatePath = candidatePath.Trim('\"', '\'').Trim();

            if (string.IsNullOrEmpty(candidatePath))
            {
                if (isExplicitSelect)
                {
                    targets.Add(new CommandLineTarget("", null, rawArg, false, "Missing path argument for /select"));
                }
                continue;
            }

            // 2. Resolve via IPathResolver (expands %USERPROFILE%, ~, shell:*, etc.)
            var resolution = pathResolver.Resolve(candidatePath);
            string resolvedPath = resolution.ResolvedPath;

            // 3. Virtual URIs (thispc://, recyclebin://)
            if (resolution.PathType == ResolvedPathType.ThisPC)
            {
                targets.Add(new CommandLineTarget("thispc://", null, rawArg, true));
                continue;
            }

            if (resolution.PathType == ResolvedPathType.RecycleBin)
            {
                targets.Add(new CommandLineTarget("recyclebin://", null, rawArg, true));
                continue;
            }

            if (resolution.PathType == ResolvedPathType.UnsupportedVirtualFolder)
            {
                targets.Add(new CommandLineTarget("", null, rawArg, false, resolution.ErrorMessage ?? $"Unsupported virtual folder: {candidatePath}"));
                continue;
            }

            // 4. Check on FileSystem
            if (isExplicitSelect)
            {
                // Must open parent directory and select the item (file or folder)
                if (fileSystem.FileExists(resolvedPath))
                {
                    string parent = Path.GetDirectoryName(resolvedPath) ?? resolvedPath;
                    string fileName = Path.GetFileName(resolvedPath);
                    targets.Add(new CommandLineTarget(parent, fileName, rawArg, true));
                }
                else if (fileSystem.DirectoryExists(resolvedPath))
                {
                    string trimmed = resolvedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string parent = Path.GetDirectoryName(trimmed) ?? "";
                    string dirName = Path.GetFileName(trimmed);
                    if (string.IsNullOrEmpty(parent))
                    {
                        // E.g. drive root "C:\" -> open it directly
                        targets.Add(new CommandLineTarget(resolvedPath, null, rawArg, true));
                    }
                    else
                    {
                        targets.Add(new CommandLineTarget(parent, dirName, rawArg, true));
                    }
                }
                else
                {
                    // Item does not exist on disk
                    targets.Add(new CommandLineTarget("", null, rawArg, false, $"The item to select does not exist: {candidatePath}"));
                }
            }
            else
            {
                // Regular argument: directory or file
                if (fileSystem.DirectoryExists(resolvedPath))
                {
                    targets.Add(new CommandLineTarget(resolvedPath, null, rawArg, true));
                }
                else if (fileSystem.FileExists(resolvedPath))
                {
                    string parent = Path.GetDirectoryName(resolvedPath) ?? resolvedPath;
                    string fileName = Path.GetFileName(resolvedPath);
                    targets.Add(new CommandLineTarget(parent, fileName, rawArg, true));
                }
                else
                {
                    // If neither exists, report as non-existent target rather than crashing
                    targets.Add(new CommandLineTarget("", null, rawArg, false, $"The path does not exist: {candidatePath}"));
                }
            }
        }

        return new CommandLineParsedResult(targets);
    }
}
