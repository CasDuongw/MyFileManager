using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FM.App.ViewModels;
using FM.Core.Helpers;
using FM.Core.Models;
using FM.Core.Navigation;

namespace FM.App.Models;

/// <summary>
/// Represents an individual tab within a FilePane.
/// Encapsulates independent navigation history, view mode, sort order, and cached items.
/// </summary>
public sealed partial class FileTabItemViewModel : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();

    [ObservableProperty]
    private string _title = "New Tab";

    [ObservableProperty]
    private string _currentPath = string.Empty;

    [ObservableProperty]
    private ImageSource? _icon;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isUnavailable;

    [ObservableProperty]
    private FilePaneViewMode _viewMode = FilePaneViewMode.Details;

    [ObservableProperty]
    private FileSortColumn _currentSortColumn = FileSortColumn.Name;

    [ObservableProperty]
    private bool _isSortAscending = true;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private SpecialPaneMode _specialMode = SpecialPaneMode.None;

    public NavigationHistory History { get; set; } = new();

    public List<FileItemViewModel>? CachedItems { get; set; }

    public HashSet<string> SelectedPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public double ScrollOffset { get; set; }

    public FileTabItemViewModel Clone()
    {
        return new FileTabItemViewModel
        {
            Title = Title,
            CurrentPath = CurrentPath,
            Icon = Icon,
            ViewMode = ViewMode,
            CurrentSortColumn = CurrentSortColumn,
            IsSortAscending = IsSortAscending,
            FilterText = FilterText,
            SpecialMode = SpecialMode,
            IsUnavailable = IsUnavailable,
            CachedItems = CachedItems != null ? new List<FileItemViewModel>(CachedItems) : null,
            SelectedPaths = new HashSet<string>(SelectedPaths, StringComparer.OrdinalIgnoreCase)
        };
    }
}
