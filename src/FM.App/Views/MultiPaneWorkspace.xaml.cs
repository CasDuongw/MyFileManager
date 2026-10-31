using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FM.App.Models;
using FM.App.ViewModels;

namespace FM.App.Views;

/// <summary>
/// Interaction logic for MultiPaneWorkspace.xaml.
/// Dynamically hosts and arranges FilePaneViews in single, dual, triple, or quad-grid layouts with interactive splitters.
/// Reuses existing FilePaneView instances across layout changes to preserve scrolling, selections, and navigation state.
/// </summary>
public partial class MultiPaneWorkspace : UserControl
{
    private readonly Dictionary<FilePaneViewModel, FilePaneView> _paneViews = new();
    private MainViewModel? _observedViewModel;

    private static readonly Brush SplitterBrush = CreateFrozenBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public MultiPaneWorkspace()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_observedViewModel != null)
        {
            _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _observedViewModel.Panes.CollectionChanged -= OnPanesCollectionChanged;
        }

        _observedViewModel = e.NewValue as MainViewModel;

        if (_observedViewModel != null)
        {
            _observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            _observedViewModel.Panes.CollectionChanged += OnPanesCollectionChanged;
        }

        RebuildLayout();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.LayoutMode))
        {
            RebuildLayout();
        }
    }

    private void OnPanesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildLayout();
    }

    public FilePaneView? GetViewForPane(FilePaneViewModel pane)
    {
        _paneViews.TryGetValue(pane, out var view);
        return view;
    }

    public void RebuildLayout()
    {
        var vm = _observedViewModel;
        if (vm == null || vm.Panes.Count == 0)
        {
            RootGrid.Children.Clear();
            RootGrid.RowDefinitions.Clear();
            RootGrid.ColumnDefinitions.Clear();
            return;
        }

        // Clean up cached views for removed panes
        var currentPanes = new HashSet<FilePaneViewModel>(vm.Panes);
        var toRemove = _paneViews.Keys.Where(p => !currentPanes.Contains(p)).ToList();
        foreach (var p in toRemove)
        {
            _paneViews.Remove(p);
        }

        // Ensure each pane has a persistent view
        foreach (var p in vm.Panes)
        {
            if (!_paneViews.ContainsKey(p))
            {
                _paneViews[p] = new FilePaneView { DataContext = p };
            }
        }

        RootGrid.Children.Clear();
        RootGrid.RowDefinitions.Clear();
        RootGrid.ColumnDefinitions.Clear();

        switch (vm.LayoutMode)
        {
            case PaneLayoutMode.Single:
                BuildSingleLayout(vm);
                break;
            case PaneLayoutMode.DualVertical:
                BuildDualVerticalLayout(vm);
                break;
            case PaneLayoutMode.DualHorizontal:
                BuildDualHorizontalLayout(vm);
                break;
            case PaneLayoutMode.TripleLeft:
                BuildTripleLeftLayout(vm);
                break;
            case PaneLayoutMode.QuadGrid:
                BuildQuadGridLayout(vm);
                break;
            default:
                BuildDualVerticalLayout(vm);
                break;
        }
    }

    private void BuildSingleLayout(MainViewModel vm)
    {
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var targetPane = vm.ActivePane ?? vm.Panes[0];
        var view = _paneViews[targetPane];
        Grid.SetRow(view, 0);
        Grid.SetColumn(view, 0);
        Grid.SetRowSpan(view, 1);
        Grid.SetColumnSpan(view, 1);
        RootGrid.Children.Add(view);
    }

    private void BuildDualVerticalLayout(MainViewModel vm)
    {
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6, GridUnitType.Pixel) });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var v0 = _paneViews[vm.Panes[0]];
        Grid.SetRow(v0, 0);
        Grid.SetColumn(v0, 0);
        Grid.SetRowSpan(v0, 1);
        Grid.SetColumnSpan(v0, 1);
        RootGrid.Children.Add(v0);

        var splitter = CreateSplitter(isVertical: true);
        Grid.SetRow(splitter, 0);
        Grid.SetColumn(splitter, 1);
        RootGrid.Children.Add(splitter);

        if (vm.Panes.Count > 1)
        {
            var v1 = _paneViews[vm.Panes[1]];
            Grid.SetRow(v1, 0);
            Grid.SetColumn(v1, 2);
            Grid.SetRowSpan(v1, 1);
            Grid.SetColumnSpan(v1, 1);
            RootGrid.Children.Add(v1);
        }
    }

    private void BuildDualHorizontalLayout(MainViewModel vm)
    {
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 120 });
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6, GridUnitType.Pixel) });
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 120 });

        var v0 = _paneViews[vm.Panes[0]];
        Grid.SetRow(v0, 0);
        Grid.SetColumn(v0, 0);
        Grid.SetRowSpan(v0, 1);
        Grid.SetColumnSpan(v0, 1);
        RootGrid.Children.Add(v0);

        var splitter = CreateSplitter(isVertical: false);
        Grid.SetRow(splitter, 1);
        Grid.SetColumn(splitter, 0);
        RootGrid.Children.Add(splitter);

        if (vm.Panes.Count > 1)
        {
            var v1 = _paneViews[vm.Panes[1]];
            Grid.SetRow(v1, 2);
            Grid.SetColumn(v1, 0);
            Grid.SetRowSpan(v1, 1);
            Grid.SetColumnSpan(v1, 1);
            RootGrid.Children.Add(v1);
        }
    }

    private void BuildTripleLeftLayout(MainViewModel vm)
    {
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star), MinWidth = 180 });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6, GridUnitType.Pixel) });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });

        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 100 });
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6, GridUnitType.Pixel) });
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 100 });

        // Left primary pane (spans both rows)
        var v0 = _paneViews[vm.Panes[0]];
        Grid.SetRow(v0, 0);
        Grid.SetColumn(v0, 0);
        Grid.SetRowSpan(v0, 3);
        Grid.SetColumnSpan(v0, 1);
        RootGrid.Children.Add(v0);

        var vSplitter = CreateSplitter(isVertical: true);
        Grid.SetRow(vSplitter, 0);
        Grid.SetColumn(vSplitter, 1);
        Grid.SetRowSpan(vSplitter, 3);
        RootGrid.Children.Add(vSplitter);

        // Right top pane
        if (vm.Panes.Count > 1)
        {
            var v1 = _paneViews[vm.Panes[1]];
            Grid.SetRow(v1, 0);
            Grid.SetColumn(v1, 2);
            Grid.SetRowSpan(v1, 1);
            Grid.SetColumnSpan(v1, 1);
            RootGrid.Children.Add(v1);
        }

        var hSplitter = CreateSplitter(isVertical: false);
        Grid.SetRow(hSplitter, 1);
        Grid.SetColumn(hSplitter, 2);
        RootGrid.Children.Add(hSplitter);

        // Right bottom pane
        if (vm.Panes.Count > 2)
        {
            var v2 = _paneViews[vm.Panes[2]];
            Grid.SetRow(v2, 2);
            Grid.SetColumn(v2, 2);
            Grid.SetRowSpan(v2, 1);
            Grid.SetColumnSpan(v2, 1);
            RootGrid.Children.Add(v2);
        }
    }

    private void BuildQuadGridLayout(MainViewModel vm)
    {
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6, GridUnitType.Pixel) });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });

        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 120 });
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6, GridUnitType.Pixel) });
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 120 });

        // Row 0 Col 0: Top-Left
        var v0 = _paneViews[vm.Panes[0]];
        Grid.SetRow(v0, 0);
        Grid.SetColumn(v0, 0);
        Grid.SetRowSpan(v0, 1);
        Grid.SetColumnSpan(v0, 1);
        RootGrid.Children.Add(v0);

        // Row 0 Col 2: Top-Right
        if (vm.Panes.Count > 1)
        {
            var v1 = _paneViews[vm.Panes[1]];
            Grid.SetRow(v1, 0);
            Grid.SetColumn(v1, 2);
            Grid.SetRowSpan(v1, 1);
            Grid.SetColumnSpan(v1, 1);
            RootGrid.Children.Add(v1);
        }

        // Row 2 Col 0: Bottom-Left
        if (vm.Panes.Count > 2)
        {
            var v2 = _paneViews[vm.Panes[2]];
            Grid.SetRow(v2, 2);
            Grid.SetColumn(v2, 0);
            Grid.SetRowSpan(v2, 1);
            Grid.SetColumnSpan(v2, 1);
            RootGrid.Children.Add(v2);
        }

        // Row 2 Col 2: Bottom-Right
        if (vm.Panes.Count > 3)
        {
            var v3 = _paneViews[vm.Panes[3]];
            Grid.SetRow(v3, 2);
            Grid.SetColumn(v3, 2);
            Grid.SetRowSpan(v3, 1);
            Grid.SetColumnSpan(v3, 1);
            RootGrid.Children.Add(v3);
        }

        // Vertical Splitter spanning all rows
        var vSplitter = CreateSplitter(isVertical: true);
        Grid.SetRow(vSplitter, 0);
        Grid.SetColumn(vSplitter, 1);
        Grid.SetRowSpan(vSplitter, 3);
        RootGrid.Children.Add(vSplitter);

        // Horizontal Splitter spanning all columns
        var hSplitter = CreateSplitter(isVertical: false);
        Grid.SetRow(hSplitter, 1);
        Grid.SetColumn(hSplitter, 0);
        Grid.SetColumnSpan(hSplitter, 3);
        RootGrid.Children.Add(hSplitter);
    }

    private static GridSplitter CreateSplitter(bool isVertical)
    {
        return new GridSplitter
        {
            Width = isVertical ? 6 : double.NaN,
            Height = isVertical ? double.NaN : 6,
            HorizontalAlignment = isVertical ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
            VerticalAlignment = isVertical ? VerticalAlignment.Stretch : VerticalAlignment.Center,
            Background = SplitterBrush,
            ShowsPreview = false,
            Cursor = isVertical ? Cursors.SizeWE : Cursors.SizeNS
        };
    }
}
