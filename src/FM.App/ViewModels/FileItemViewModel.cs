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
    private readonly CancellationToken _cancellationToken;

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
        CancellationToken cancellationToken = default)
    {
        Entry = entry;
        _iconService = iconService;
        _cancellationToken = cancellationToken;
    }

    private async Task RequestIconAsync()
    {
        if (_iconService == null) return;
        if (_cancellationToken.IsCancellationRequested)
        {
            _iconRequested = false;
            return;
        }

        try
        {
            var icon = await _iconService.GetIconAsync(FullPath, IsDirectory, isDrive: false, _cancellationToken);
            if (!_cancellationToken.IsCancellationRequested && icon != null)
            {
                Icon = icon;
            }
            else if (_cancellationToken.IsCancellationRequested)
            {
                _iconRequested = false;
            }
        }
        catch (OperationCanceledException)
        {
            _iconRequested = false;
        }
        catch
        {
            // Ignore missing icon
        }
    }
}
