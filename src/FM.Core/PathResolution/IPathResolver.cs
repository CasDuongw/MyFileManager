namespace FM.Core.PathResolution;

/// <summary>
/// Resolves raw input strings (environment variables, tilde, shell: names, UNC paths, This PC, Recycle Bin)
/// into actionable paths or special locations.
/// </summary>
public interface IPathResolver
{
    PathResolutionResult Resolve(string inputPath);
}

