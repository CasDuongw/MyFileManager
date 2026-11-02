using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FM.Core.Drives;

namespace FM.App.ViewModels;

/// <summary>
/// ViewModel representing a storage drive card displayed within the "This PC" view.
/// </summary>
public partial class DriveCardViewModel : ObservableObject
{
    public DriveItem Drive { get; }

    public string Name => Drive.DisplayName;
    public string Path => Drive.Path;
    public string VolumeLabel => Drive.VolumeLabel;
    public string DriveLetter => Drive.DriveLetter;
    public DriveType DriveType => Drive.DriveType;
    public double PercentUsed => Drive.PercentUsed;
    public string FreeSpaceText => Drive.FormattedFreeSpaceText;
    public string FileSystem => Drive.FileSystem;
    public bool IsRemovable => Drive.IsRemovable;
    public bool IsWarningSpace => PercentUsed >= 90.0;

    [ObservableProperty]
    private ImageSource? _icon;

    public IRelayCommand OpenCommand { get; }
    public IRelayCommand? EjectCommand { get; }

    public DriveCardViewModel(
        DriveItem drive,
        Action<string> onOpen,
        Action<string>? onEject = null,
        ImageSource? icon = null)
    {
        Drive = drive;
        _icon = icon;
        OpenCommand = new RelayCommand(() => onOpen(Drive.Path));
        if (IsRemovable && onEject != null)
        {
            EjectCommand = new RelayCommand(() => onEject(Drive.Path));
        }
    }
}

