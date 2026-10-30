using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FM.App.Helpers;
using FM.App.Services;
using FM.Core.Models;

namespace FM.App.ViewModels;

/// <summary>
/// ViewModel representing an individual file or directory item in the file list.
/// </summary>
public partial class FileItemViewModel : ObservableObject
{
    public FileEntry Entry { get; }

    public string Name => Entry.Name;
    public string FullPath => Entry.FullPath;
    public bool IsDirectory => Entry.IsDirectory;
    public long Size => Entry.Size;
    public DateTime Modified => Entry.Modified;

    public string FormattedSize => FormatHelper.FormatFileSize(Entry.Size, Entry.IsDirectory);
    public string FormattedModified => Entry.Modified == DateTime.MinValue ? string.Empty : Entry.Modified.ToString("yyyy-MM-dd HH:mm");
    public string DisplayType => FormatHelper.FormatFileType(Entry.Name, Entry.IsDirectory);

    [ObservableProperty]
    private ImageSource? _icon;

    public FileItemViewModel(FileEntry entry)
    {
        Entry = entry;
    }

    /// <summary>
    /// Asynchronously populates the item's icon from the icon service.
    /// </summary>
    public async Task LoadIconAsync(IIconService iconService, CancellationToken ct)
    {
        try
        {
            var icon = await iconService.GetIconAsync(FullPath, IsDirectory, ct);
            if (!ct.IsCancellationRequested && icon != null)
            {
                Icon = icon;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when user quickly navigates
        }
        catch
        {
            // Non-critical if an individual icon cannot be loaded
        }
    }
}
