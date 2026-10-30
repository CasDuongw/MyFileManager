using System.IO;
using FM.Core.FileSystem;

namespace FM.Core.Operations;

/// <summary>
/// Reversible operation that creates a new directory.
/// Undo only deletes the directory if it is still empty, protecting user files.
/// </summary>
public sealed class CreateFolderOperation : IFileOperation
{
    private readonly IFileSystem _fs;
    public string ParentDirectory { get; }
    public string FolderName { get; }
    public string TargetPath { get; }

    public string Description => $"Create folder '{FolderName}'";

    public CreateFolderOperation(IFileSystem fs, string parentDirectory, string folderName)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        ParentDirectory = parentDirectory ?? throw new ArgumentNullException(nameof(parentDirectory));
        FolderName = folderName ?? throw new ArgumentNullException(nameof(folderName));
        TargetPath = Path.Combine(ParentDirectory, FolderName);
    }

    public static string GetDefaultUniqueFolderName(IFileSystem fs, string parentDirectory, string baseName = "New folder")
    {
        string candidate = Path.Combine(parentDirectory, baseName);
        if (!fs.Exists(candidate))
        {
            return baseName;
        }

        for (int i = 2; ; i++)
        {
            string name = $"{baseName} ({i})";
            string path = Path.Combine(parentDirectory, name);
            if (!fs.Exists(path))
            {
                return name;
            }
        }
    }

    public Task<OperationResult> DoAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!FileNameValidator.TryValidate(FolderName, out string? error))
        {
            return Task.FromResult(OperationResult.Fail(error!));
        }

        if (_fs.Exists(TargetPath))
        {
            return Task.FromResult(OperationResult.Fail($"A file or folder with the name '{FolderName}' already exists."));
        }

        try
        {
            _fs.CreateDirectory(TargetPath);
            return Task.FromResult(OperationResult.Ok(TargetPath));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail($"Failed to create folder: {ex.Message}"));
        }
    }

    public Task<OperationResult> UndoAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!_fs.DirectoryExists(TargetPath))
        {
            return Task.FromResult(OperationResult.Fail($"Cannot undo: Folder '{FolderName}' does not exist."));
        }

        if (!_fs.IsDirectoryEmpty(TargetPath))
        {
            return Task.FromResult(OperationResult.Fail($"Cannot undo: Folder '{FolderName}' is not empty."));
        }

        try
        {
            _fs.DeleteDirectory(TargetPath, recursive: false);
            return Task.FromResult(OperationResult.Ok(ParentDirectory));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail($"Failed to delete folder on undo: {ex.Message}"));
        }
    }
}

