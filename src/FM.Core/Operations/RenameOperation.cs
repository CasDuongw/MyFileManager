using System.IO;
using FM.Core.FileSystem;

namespace FM.Core.Operations;

/// <summary>
/// Reversible operation that renames a file or directory.
/// Checks for collisions prior to executing and undoing.
/// </summary>
public sealed class RenameOperation : IFileOperation
{
    private readonly IFileSystem _fs;
    public string SourcePath { get; }
    public string NewName { get; }
    public string OldName { get; }
    public string TargetPath { get; }
    public bool IsDirectory { get; }

    public string Description => $"Rename '{(IsDirectory ? OldName : OldName)}' to '{NewName}'";

    public RenameOperation(IFileSystem fs, string sourcePath, string newName, bool isDirectory)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        SourcePath = sourcePath ?? throw new ArgumentNullException(nameof(sourcePath));
        NewName = newName ?? throw new ArgumentNullException(nameof(newName));
        IsDirectory = isDirectory;

        OldName = Path.GetFileName(sourcePath);
        string? parent = Path.GetDirectoryName(sourcePath);
        TargetPath = !string.IsNullOrEmpty(parent) ? Path.Combine(parent, newName) : newName;
    }

    public Task<OperationResult> DoAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.Equals(OldName, NewName, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(OperationResult.Fail("The new name is the same as the current name."));
        }

        if (!FileNameValidator.TryValidate(NewName, out string? error))
        {
            return Task.FromResult(OperationResult.Fail(error!));
        }

        if (IsDirectory)
        {
            if (!_fs.DirectoryExists(SourcePath))
            {
                return Task.FromResult(OperationResult.Fail($"Directory '{OldName}' does not exist."));
            }
        }
        else
        {
            if (!_fs.FileExists(SourcePath))
            {
                return Task.FromResult(OperationResult.Fail($"File '{OldName}' does not exist."));
            }
        }

        if (_fs.Exists(TargetPath))
        {
            return Task.FromResult(OperationResult.Fail($"An item named '{NewName}' already exists in this location."));
        }

        try
        {
            if (IsDirectory)
            {
                _fs.MoveDirectory(SourcePath, TargetPath);
            }
            else
            {
                _fs.MoveFile(SourcePath, TargetPath);
            }

            return Task.FromResult(OperationResult.Ok(TargetPath));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail($"Failed to rename: {ex.Message}"));
        }
    }

    public Task<OperationResult> UndoAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (IsDirectory)
        {
            if (!_fs.DirectoryExists(TargetPath))
            {
                return Task.FromResult(OperationResult.Fail($"Cannot undo: '{NewName}' no longer exists."));
            }
        }
        else
        {
            if (!_fs.FileExists(TargetPath))
            {
                return Task.FromResult(OperationResult.Fail($"Cannot undo: '{NewName}' no longer exists."));
            }
        }

        if (_fs.Exists(SourcePath))
        {
            return Task.FromResult(OperationResult.Fail($"Cannot undo: An item named '{OldName}' already exists."));
        }

        try
        {
            if (IsDirectory)
            {
                _fs.MoveDirectory(TargetPath, SourcePath);
            }
            else
            {
                _fs.MoveFile(TargetPath, SourcePath);
            }

            return Task.FromResult(OperationResult.Ok(SourcePath));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail($"Failed to undo rename: {ex.Message}"));
        }
    }
}
