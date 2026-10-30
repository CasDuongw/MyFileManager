using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FM.App.ViewModels;

public enum SidebarItemCategory
{
    Favorite,
    Drive
}

/// <summary>
/// ViewModel representing a shortcut or drive in the navigation sidebar.
/// </summary>
public partial class SidebarItemViewModel : ObservableObject
{
    public string Name { get; }
    public string Path { get; }
    public SidebarItemCategory Category { get; }

    [ObservableProperty]
    private ImageSource? _icon;

    public SidebarItemViewModel(string name, string path, SidebarItemCategory category, ImageSource? icon = null)
    {
        Name = name;
        Path = path;
        Category = category;
        _icon = icon;
    }
}
