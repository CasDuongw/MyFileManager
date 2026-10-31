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

    private FilePaneView GetOrCreateView(FilePaneViewModel pane)
    {
        if (!_paneViews.TryGetValue(pane, out var view))
        {
            view = new FilePaneView { DataContext = pane };
            _paneViews[pane] = view;
        }
        return view;
    }

    private void BuildEmptyState(MainViewModel? vm)
    {
        RootGrid.Children.Clear();
        RootGrid.RowDefinitions.Clear();
        RootGrid.ColumnDefinitions.Clear();

        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var cardBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(2),
            SnapsToDevicePixels = true
        };

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 420
        };

        var iconText = new TextBlock
        {
            Text = "📂",
            FontSize = 48,
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.65,
            Margin = new Thickness(0, 0, 0, 16)
        };

        var titleText = new TextBlock
        {
            Text = "Không có thư mục nào đang mở",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var descText = new TextBlock
        {
            Text = "Chọn một thư mục từ danh sách bên trái (Favorites / Drives) hoặc bấm nút bên dưới để mở thẻ mới.",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 20)
        };

        var newTabBtn = new Button
        {
            Content = "+ Mở thẻ mới",
            Height = 32,
            Padding = new Thickness(16, 4, 16, 4),
            Background = new SolidColorBrush(Color.FromRgb(0xD9, 0x26, 0x62)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var btnTemplate = new ControlTemplate(typeof(Button));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
        var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
        contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        borderFactory.AppendChild(contentPresenter);
        btnTemplate.VisualTree = borderFactory;
        newTabBtn.Template = btnTemplate;

        newTabBtn.Click += (s, e) =>
        {
            vm?.OpenNewTab();
        };

        var hintText = new TextBlock
        {
            Text = "Phím tắt: Ctrl+T để mở nhanh thẻ mới",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0)
        };

        stack.Children.Add(iconText);
        stack.Children.Add(titleText);
        stack.Children.Add(descText);
        stack.Children.Add(newTabBtn);
        stack.Children.Add(hintText);

        cardBorder.Child = stack;

        Grid.SetRow(cardBorder, 0);
        Grid.SetColumn(cardBorder, 0);
        RootGrid.Children.Add(cardBorder);
    }

    public void RebuildLayout()
    {
        var vm = _observedViewModel;
        if (vm == null || vm.Panes.Count == 0)
        {
            BuildEmptyState(vm);
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
                if (vm.Panes.Count > 4)
                {
                    BuildDynamicGridLayout(vm);
                }
                else
                {
                    BuildQuadGridLayout(vm);
                }
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

        var targetPane = (vm.ActivePane != null && vm.Panes.Contains(vm.ActivePane))
            ? vm.ActivePane
            : vm.Panes.FirstOrDefault();

        if (targetPane == null)
        {
            BuildEmptyState(vm);
            return;
        }

        var view = GetOrCreateView(targetPane);
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

        var v0 = GetOrCreateView(vm.Panes[0]);
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
            var v1 = GetOrCreateView(vm.Panes[1]);
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

        var v0 = GetOrCreateView(vm.Panes[0]);
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
            var v1 = GetOrCreateView(vm.Panes[1]);
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
        var v0 = GetOrCreateView(vm.Panes[0]);
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
            var v1 = GetOrCreateView(vm.Panes[1]);
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
            var v2 = GetOrCreateView(vm.Panes[2]);
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
        var v0 = GetOrCreateView(vm.Panes[0]);
        Grid.SetRow(v0, 0);
        Grid.SetColumn(v0, 0);
        Grid.SetRowSpan(v0, 1);
        Grid.SetColumnSpan(v0, 1);
        RootGrid.Children.Add(v0);

        // Row 0 Col 2: Top-Right
        if (vm.Panes.Count > 1)
        {
            var v1 = GetOrCreateView(vm.Panes[1]);
            Grid.SetRow(v1, 0);
            Grid.SetColumn(v1, 2);
            Grid.SetRowSpan(v1, 1);
            Grid.SetColumnSpan(v1, 1);
            RootGrid.Children.Add(v1);
        }

        // Row 2 Col 0: Bottom-Left
        if (vm.Panes.Count > 2)
        {
            var v2 = GetOrCreateView(vm.Panes[2]);
            Grid.SetRow(v2, 2);
            Grid.SetColumn(v2, 0);
            Grid.SetRowSpan(v2, 1);
            Grid.SetColumnSpan(v2, 1);
            RootGrid.Children.Add(v2);
        }

        // Row 2 Col 2: Bottom-Right
        if (vm.Panes.Count > 3)
        {
            var v3 = GetOrCreateView(vm.Panes[3]);
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


    private void BuildDynamicGridLayout(MainViewModel vm)
    {
        int count = vm.Panes.Count;
        if (count == 5)
        {
            BuildFivePanesLayout(vm);
            return;
        }

        int cols = (count <= 6) ? 3 : 4;
        int rows = (int)Math.Ceiling((double)count / cols);

        // Columns and splitters
        for (int i = 0; i < cols; i++)
        {
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
            if (i < cols - 1)
            {
                RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6, GridUnitType.Pixel) });
            }
        }

        // Rows and splitters
        for (int i = 0; i < rows; i++)
        {
            RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 90 });
            if (i < rows - 1)
            {
                RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6, GridUnitType.Pixel) });
            }
        }

        // Panes
        for (int i = 0; i < count; i++)
        {
            int r = i / cols;
            int col = i % cols;
            int gridRow = r * 2;
            int gridCol = col * 2;

            var view = GetOrCreateView(vm.Panes[i]);
            Grid.SetRow(view, gridRow);
            Grid.SetColumn(view, gridCol);
            Grid.SetRowSpan(view, 1);
            Grid.SetColumnSpan(view, 1);
            RootGrid.Children.Add(view);
        }

        // Vertical splitters
        int totalGridRows = rows * 2 - 1;
        for (int i = 0; i < cols - 1; i++)
        {
            var vSplitter = CreateSplitter(isVertical: true);
            Grid.SetRow(vSplitter, 0);
            Grid.SetColumn(vSplitter, i * 2 + 1);
            Grid.SetRowSpan(vSplitter, totalGridRows);
            RootGrid.Children.Add(vSplitter);
        }

        // Horizontal splitters
        int totalGridCols = cols * 2 - 1;
        for (int i = 0; i < rows - 1; i++)
        {
            var hSplitter = CreateSplitter(isVertical: false);
            Grid.SetRow(hSplitter, i * 2 + 1);
            Grid.SetColumn(hSplitter, 0);
            Grid.SetColumnSpan(hSplitter, totalGridCols);
            RootGrid.Children.Add(hSplitter);
        }
    }

    private void BuildFivePanesLayout(MainViewModel vm)
    {
        // 3 columns: Col 0 (1 large pane), Col 1 (2 stacked), Col 2 (2 stacked)
        // Matching exact 5-pane layout in user demonstration
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star), MinWidth = 140 });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6, GridUnitType.Pixel) });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 140 });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6, GridUnitType.Pixel) });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 140 });

        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 100 });
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6, GridUnitType.Pixel) });
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 100 });

        // Left Col 0: Pane 0 full height (spans Row 0 to 2)
        var v0 = GetOrCreateView(vm.Panes[0]);
        Grid.SetRow(v0, 0);
        Grid.SetColumn(v0, 0);
        Grid.SetRowSpan(v0, 3);
        Grid.SetColumnSpan(v0, 1);
        RootGrid.Children.Add(v0);

        // Col 2 Top: Pane 1
        var v1 = GetOrCreateView(vm.Panes[1]);
        Grid.SetRow(v1, 0);
        Grid.SetColumn(v1, 2);
        Grid.SetRowSpan(v1, 1);
        Grid.SetColumnSpan(v1, 1);
        RootGrid.Children.Add(v1);

        // Col 2 Bottom: Pane 2
        var v2 = GetOrCreateView(vm.Panes[2]);
        Grid.SetRow(v2, 2);
        Grid.SetColumn(v2, 2);
        Grid.SetRowSpan(v2, 1);
        Grid.SetColumnSpan(v2, 1);
        RootGrid.Children.Add(v2);

        // Col 4 Top: Pane 3
        var v3 = GetOrCreateView(vm.Panes[3]);
        Grid.SetRow(v3, 0);
        Grid.SetColumn(v3, 4);
        Grid.SetRowSpan(v3, 1);
        Grid.SetColumnSpan(v3, 1);
        RootGrid.Children.Add(v3);

        // Col 4 Bottom: Pane 4
        var v4 = GetOrCreateView(vm.Panes[4]);
        Grid.SetRow(v4, 2);
        Grid.SetColumn(v4, 4);
        Grid.SetRowSpan(v4, 1);
        Grid.SetColumnSpan(v4, 1);
        RootGrid.Children.Add(v4);

        // Vertical splitters
        var vs1 = CreateSplitter(isVertical: true);
        Grid.SetRow(vs1, 0);
        Grid.SetColumn(vs1, 1);
        Grid.SetRowSpan(vs1, 3);
        RootGrid.Children.Add(vs1);

        var vs2 = CreateSplitter(isVertical: true);
        Grid.SetRow(vs2, 0);
        Grid.SetColumn(vs2, 3);
        Grid.SetRowSpan(vs2, 3);
        RootGrid.Children.Add(vs2);

        // Horizontal splitters for middle and right columns
        var hs1 = CreateSplitter(isVertical: false);
        Grid.SetRow(hs1, 1);
        Grid.SetColumn(hs1, 2);
        RootGrid.Children.Add(hs1);

        var hs2 = CreateSplitter(isVertical: false);
        Grid.SetRow(hs2, 1);
        Grid.SetColumn(hs2, 4);
        RootGrid.Children.Add(hs2);
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

