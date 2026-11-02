using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FM.Core.Drives;

namespace FM.App.ViewModels;

public enum SidebarItemCategory
{
    Favorite,
    Drive,
    Special
}

/// <summary>
/// ViewModel representing a shortcut, special location, or storage drive in the navigation sidebar.
/// </summary>
public partial class SidebarItemViewModel : ObservableObject
{
    public string Name { get; }
    public string Path { get; }
    public SidebarItemCategory Category { get; }
    public DriveItem? DriveInfo { get; }

    public bool HasDriveInfo => DriveInfo != null && DriveInfo.IsReady;
    public double PercentUsed => DriveInfo?.PercentUsed ?? 0.0;
    public string? FreeSpaceText => DriveInfo?.FormattedFreeSpaceText;
    public bool IsRemovable => DriveInfo?.IsRemovable ?? false;

    [ObservableProperty]
    private ImageSource? _icon;

    public ICommand? EjectCommand { get; set; }

    public SidebarItemViewModel(
        string name,
        string path,
        SidebarItemCategory category,
        ImageSource? icon = null,
        DriveItem? driveInfo = null,
        ICommand? ejectCommand = null)
    {
        Name = name;
        Path = path;
        Category = category;
        _icon = icon;
        DriveInfo = driveInfo;
        EjectCommand = ejectCommand;
    }
}
