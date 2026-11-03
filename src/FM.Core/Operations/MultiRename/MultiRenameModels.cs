namespace FM.Core.Operations.MultiRename;

public enum ExtensionCase
{
    Keep,
    Lower,
    Upper,
    Title
}

public enum MultiRenameStatus
{
    Valid,
    Unchanged,
    InvalidName,
    DiskCollision,
    BatchCollision
}

public sealed record MultiRenameItemInput(
    string FullPath,
    string Name,
    bool IsDirectory,
    DateTime DateModified);

public sealed record MultiRenamePlanItem(
    string OriginalPath,
    string OriginalName,
    string NewName,
    string NewPath,
    MultiRenameStatus Status,
    string? ErrorMessage = null,
    bool IsDirectory = false)
{
    public bool IsValid => Status == MultiRenameStatus.Valid || Status == MultiRenameStatus.Unchanged;
    public bool HasCollision => Status == MultiRenameStatus.DiskCollision || Status == MultiRenameStatus.BatchCollision;
    public bool HasError => Status == MultiRenameStatus.InvalidName || HasCollision;
    public bool IsChanged => Status == MultiRenameStatus.Valid && !string.Equals(OriginalName, NewName, StringComparison.Ordinal);
}

public sealed record MultiRenameOptions(
    string Pattern = "[N].[E]",
    ExtensionCase ExtCase = ExtensionCase.Keep,
    int CounterStart = 1,
    int CounterStep = 1,
    int CounterPadding = 1,
    string? FindText = null,
    string? ReplaceText = null,
    bool MatchCase = false,
    bool UseRegex = false);

public sealed record MultiRenamePlan(IReadOnlyList<MultiRenamePlanItem> Items)
{
    public bool HasErrors => Items.Any(i => i.HasError);
    public int ErrorCount => Items.Count(i => i.HasError);
    public int TotalChanged => Items.Count(i => i.IsChanged && i.IsValid);
    public bool CanExecute => !HasErrors && TotalChanged > 0;
}

