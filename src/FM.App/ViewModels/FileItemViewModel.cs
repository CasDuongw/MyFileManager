using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FM.App.Helpers;
using FM.App.Services;
using FM.Core.Models;

namespace FM.App.ViewModels;

/// <summary>
/// ViewModel representing an individual file or directory item in the file list.
/// Supports lazy, on-demand icon retrieval: icon is only loaded when the row is realized by WPF.
/// </summary>
public partial class FileItemViewModel : ObservableObject
{
    public FileEntry Entry { get; }
    private readonly IIconService? _iconService;
    private readonly Func<CancellationToken>? _getCancellationToken;

    private bool _iconRequested;
    private ImageSource? _icon;

    public ImageSource? Icon
    {
        get
        {
            // Only request the icon when WPF virtualization realizes this row and queries the property
            if (_icon == null && !_iconRequested && _iconService != null)
            {
                _iconRequested = true;
                _ = RequestIconAsync();
            }
            return _icon;
        }
        set => SetProperty(ref _icon, value);
    }

    public string Name => Entry.Name;
    public string FullPath => Entry.FullPath;
    public bool IsDirectory => Entry.IsDirectory;
    public long Size => Entry.Size;
    public DateTime Modified => Entry.Modified;

    public string FormattedSize => FormatHelper.FormatFileSize(Entry.Size, Entry.IsDirectory);
    public string FormattedModified => Entry.Modified == DateTime.MinValue ? string.Empty : Entry.Modified.ToString("yyyy-MM-dd HH:mm");
    public string DisplayType => FormatHelper.FormatFileType(Entry.Name, Entry.IsDirectory);

    public FileItemViewModel(
        FileEntry entry,
        IIconService? iconService = null,
        Func<CancellationToken>? getCancellationToken = null)
    {
        Entry = entry;
        _iconService = iconService;
        _getCancellationToken = getCancellationToken;
    }

    private async Task RequestIconAsync()
    {
        if (_iconService == null) return;
        var ct = _getCancellationToken?.Invoke() ?? CancellationToken.None;

        try
        {
            var icon = await _iconService.GetIconAsync(FullPath, IsDirectory, isDrive: false, ct);
            if (!ct.IsCancellationRequested && icon != null)
            {
                Icon = icon;
            }
        }
        catch
        {
            // Ignore cancellation or missing icon
        }
    }
}
