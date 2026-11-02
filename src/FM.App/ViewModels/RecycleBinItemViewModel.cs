using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FM.Core.RecycleBin;

namespace FM.App.ViewModels;

/// <summary>
/// ViewModel representing an individual deleted file or folder in the Recycle Bin view.
/// </summary>
public partial class RecycleBinItemViewModel : ObservableObject
{
    public RecycleBinItem Item { get; }

    public string Id => Item.Id;
    public string Name => Item.Name;
    public string OriginalLocation => Item.OriginalLocation;
    public DateTime DateDeleted => Item.DateDeleted;
    public string FormattedDateDeleted => Item.FormattedDateDeleted;
    public long Size => Item.Size;
    public string FormattedSize => Item.FormattedSize;
    public string ItemType => Item.ItemType;
    public bool IsDirectory => Item.IsDirectory;

    [ObservableProperty]
    private ImageSource? _icon;

    public IAsyncRelayCommand RestoreCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }

    public RecycleBinItemViewModel(
        RecycleBinItem item,
        Func<string, Task> onRestore,
        Func<string, Task> onDelete,
        ImageSource? icon = null)
    {
        Item = item;
        _icon = icon;
        RestoreCommand = new AsyncRelayCommand(() => onRestore(Item.Id));
        DeleteCommand = new AsyncRelayCommand(() => onDelete(Item.Id));
    }
}
