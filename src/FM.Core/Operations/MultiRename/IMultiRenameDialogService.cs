using FM.Core.FileSystem;

namespace FM.Core.Operations.MultiRename;

/// <summary>
/// Service abstraction for presenting the Multi-Rename dialog to the user.
/// </summary>
public interface IMultiRenameDialogService
{
    Task<bool> ShowDialogAsync(
        IReadOnlyList<MultiRenameItemInput> items,
        IFileSystem fileSystem,
        Func<MultiRenamePlan, Task<OperationResult>> executeCallback,
        CancellationToken ct = default);
}

