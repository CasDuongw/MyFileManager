namespace FM.Core.ShellIntegration;

using FM.Core.FileSystem;

public enum ShellNewKind
{
    NullFile,
    FileName,
    Data
}

/// <summary>
/// Represents a template item found in Windows ShellNew registry.
/// </summary>
public record ShellNewTemplate(
    string Extension,
    string DisplayName,
    ShellNewKind Kind,
    string? TemplateFileName = null,
    byte[]? TemplateData = null);

/// <summary>
/// Service abstraction for enumerating ShellNew templates and creating files from them.
/// </summary>
public interface IShellNewService
{
    IReadOnlyList<ShellNewTemplate> GetTemplates();
    Task<string> CreateFileFromTemplateAsync(
        ShellNewTemplate template,
        string destinationDirectory,
        IFileSystem fileSystem,
        CancellationToken ct = default);
}

