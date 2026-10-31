using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using FM.App.Helpers;
using FM.App.ViewModels;
using FM.App.Models;

namespace FM.App.Views;

/// <summary>
/// Interaction logic for FilePaneView.xaml.
/// Strictly handles view-specific concerns (focus, input dispatch to ViewModel).
/// </summary>
public partial class FilePaneView : UserControl
{
    public FilePaneViewModel? ViewModel => DataContext as FilePaneViewModel;
    private bool _isSyncingSelection;
    private Point? _dragStartPoint;
    private ListViewItem? _deferredSelectionItem;
    private bool _isDragging;
    private ListViewItem? _lastHoverItem;
    private Brush? _lastHoverOriginalBackground;
    internal static bool LastDropWasInternal { get; set; }

    private static readonly Brush HighlightBrush = CreateFrozenBrush(Color.FromRgb(0xD9, 0x26, 0x62));
    private static readonly Brush ItemHoverHighlightBrush = CreateFrozenBrush(Color.FromArgb(0x40, 0x00, 0x78, 0xD4));
    private static readonly Brush InactivePaneBorderBrush = CreateFrozenBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));

    private DragAdorner? _currentTabAdorner;
    private DragAdorner? _currentFileAdorner;

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public FilePaneView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is FilePaneViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is FilePaneViewModel newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilePaneViewModel.SelectedItems) && !_isSyncingSelection && ViewModel != null)
        {
            _isSyncingSelection = true;
            try
            {
                FileListView.SelectedItems.Clear();
                foreach (var item in ViewModel.SelectedItems)
                {
                    FileListView.SelectedItems.Add(item);
                }

                if (IconsListView != null)
                {
                    IconsListView.SelectedItems.Clear();
                    foreach (var item in ViewModel.SelectedItems)
                    {
                        IconsListView.SelectedItems.Add(item);
                    }
                }
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }
    }

    public void FocusAddressBar()
    {
        if (ViewModel != null)
        {
            ViewModel.IsEditingAddress = true;
        }
        Dispatcher.BeginInvoke(() =>
        {
            AddressTextBox.Focus();
            AddressTextBox.SelectAll();
        });
    }

    public void FocusFileList()
    {
        FileListView.Focus();
    }

    private void OnPanePreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.NotifyActivated();
    }

    private void OnAddressTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel?.NavigateCommand.Execute(AddressTextBox.Text);
            if (ViewModel != null)
            {
                ViewModel.IsEditingAddress = false;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (ViewModel != null)
            {
                ViewModel.IsEditingAddress = false;
            }
            e.Handled = true;
        }
    }

    private async void OnGridViewColumnHeaderClicked(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is GridViewColumnHeader header && header.Tag is string columnTag && ViewModel != null)
        {
            var scrollViewer = FileListView.FindVisualChild<ScrollViewer>();
            double scrollOffset = scrollViewer?.VerticalOffset ?? 0;

            await ViewModel.Sort(columnTag);

            if (scrollViewer != null)
            {
                scrollViewer.ScrollToVerticalOffset(scrollOffset);
            }

            if (ViewModel.SelectedItem != null)
            {
                FileListView.ScrollIntoView(ViewModel.SelectedItem);
            }
        }
    }

    private void OnFileListViewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        // Ensure user double-clicked an actual ListViewItem, ignoring headers, scrollbars, and blank space
        var depObj = e.OriginalSource as DependencyObject;
        var listViewItem = depObj.FindVisualAncestor<ListViewItem>();

        if (listViewItem != null && listViewItem.DataContext is FileItemViewModel item)
        {
            ViewModel?.OpenItemCommand.Execute(item);
            e.Handled = true;
        }
    }

    private void OnFileListViewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                ViewModel?.OpenPropertiesCommand.Execute(null);
                e.Handled = true;
            }
            else
            {
                var activeItem = (FileListView.IsVisible ? FileListView.SelectedItem : IconsListView.SelectedItem) as FileItemViewModel;
                if (activeItem != null)
                {
                    ViewModel?.OpenItemCommand.Execute(activeItem);
                    e.Handled = true;
                }
            }
        }
        else if (e.Key == Key.Back)
        {
            if (ViewModel?.GoUpCommand.CanExecute(null) == true)
            {
                ViewModel.GoUpCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Delete)
        {
            ViewModel?.DeleteToRecycleBinCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            ViewModel?.RenameCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            ViewModel?.RefreshCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                ViewModel?.CopyPathCommand.Execute(null);
            }
            else
            {
                ViewModel?.CopyCommand.Execute(null);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.X && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            ViewModel?.CutCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            ViewModel?.PasteCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnFileListViewPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var depObj = e.OriginalSource as DependencyObject;
        var item = depObj?.FindVisualAncestor<ListViewItem>();
        if (item?.DataContext is FileItemViewModel fileItem)
        {
            if (ViewModel != null && !ViewModel.SelectedItems.Contains(fileItem))
            {
                ViewModel.UpdateSelectedItems(new[] { fileItem });
                item.IsSelected = true;
            }
        }
    }

    private void OnItemContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.ContextMenu != null)
        {
            fe.ContextMenu.DataContext = ViewModel;
        }
    }

    private void OnListViewContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // If clicking on a child ListViewItem, let the item's context menu handle it
        var depObj = e.OriginalSource as DependencyObject;
        var item = depObj?.FindVisualAncestor<ListViewItem>();
        if (item != null)
        {
            // Item context menu will open
            return;
        }

        if (sender is FrameworkElement fe && fe.ContextMenu != null)
        {
            fe.ContextMenu.DataContext = ViewModel;
        }
    }

    private void OnTopActionButtonClick(object sender, RoutedEventArgs e)
    {
        var btn = sender as FrameworkElement;
        var cm = btn?.FindVisualAncestor<ContextMenu>();
        if (cm != null)
        {
            cm.IsOpen = false;
        }
    }

    private void OnFileListViewSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingSelection || ViewModel == null) return;

        _isSyncingSelection = true;
        try
        {
            var selected = FileListView.SelectedItems.Cast<FileItemViewModel>().ToList();
            ViewModel.UpdateSelectedItems(selected);
        }
        finally
        {
            _isSyncingSelection = false;
        }
    }

    private void OnFileListViewPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var depObj = e.OriginalSource as DependencyObject;
        var item = depObj?.FindVisualAncestor<ListViewItem>();

        if (item != null)
        {
            _dragStartPoint = e.GetPosition(FileListView);

            // Preserve multi-selection: if clicked item is already selected and no Ctrl/Shift,
            // defer deselection of other items until MouseUp so user can drag all selected items.
            if (item.IsSelected &&
                !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
                !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) &&
                FileListView.SelectedItems.Count > 1)
            {
                _deferredSelectionItem = item;
                e.Handled = true;
            }
        }
        else
        {
            _dragStartPoint = null;
            _deferredSelectionItem = null;
        }
    }

    private void OnFileListViewPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_deferredSelectionItem != null)
        {
            var item = _deferredSelectionItem;
            _deferredSelectionItem = null;
            FileListView.SelectedItems.Clear();
            item.IsSelected = true;
        }

        _dragStartPoint = null;
    }

    private void OnFileListViewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging || _dragStartPoint == null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point currentPoint = e.GetPosition(FileListView);
        Vector diff = _dragStartPoint.Value - currentPoint;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            StartDrag();
        }
    }

    private void StartDrag()
    {
        if (ViewModel == null) return;

        var selected = ViewModel.SelectedItems.Select(x => x.FullPath).ToArray();
        if (selected.Length == 0 && FileListView.SelectedItem is FileItemViewModel single)
        {
            selected = new[] { single.FullPath };
        }

        if (selected.Length == 0) return;

        _deferredSelectionItem = null;
        _isDragging = true;

        var mainVm = Window.GetWindow(this)?.DataContext as MainViewModel;
        string paneId = (mainVm != null && ViewModel != null) ? mainVm.Panes.IndexOf(ViewModel).ToString() : "0";

        var data = new DataObject(DataFormats.FileDrop, selected);
        data.SetData("FM.App.PaneDrag", paneId);
        LastDropWasInternal = false;

        Window? window = Window.GetWindow(this);
        UIElement? rootElement = window?.Content as UIElement ?? window;
        AdornerLayer? layer = rootElement != null ? AdornerLayer.GetAdornerLayer(rootElement) : null;

        if (layer != null && rootElement != null)
        {
            var ghostBorder = CreateFileDragVisual(selected);
            _currentFileAdorner = new DragAdorner(rootElement, ghostBorder);
            layer.Add(_currentFileAdorner);
            _currentFileAdorner.UpdatePosition(12, 12);
            DragDrop.AddGiveFeedbackHandler(FileListView, OnFileGiveFeedback);
        }

        try
        {
            DragDrop.DoDragDrop(FileListView, data, DragDropEffects.Copy | DragDropEffects.Move);
        }
        catch
        {
            // Guard against OLE COM exceptions during drag
        }
        finally
        {
            _isDragging = false;
            _dragStartPoint = null;
            _deferredSelectionItem = null;

            if (_currentFileAdorner != null)
            {
                DragDrop.RemoveGiveFeedbackHandler(FileListView, OnFileGiveFeedback);
                layer?.Remove(_currentFileAdorner);
                _currentFileAdorner = null;
            }
        }

        // After DoDragDrop returns: refresh all panes for external drops (e.g. to Explorer).
        // For internal drops, the transfer completes asynchronously and refreshes upon completion.
        if (!LastDropWasInternal && mainVm != null)
        {
            foreach (var p in mainVm.Panes)
            {
                _ = p.RefreshAsync();
            }
        }
    }

    private void OnFileListViewDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("FM.App.TabDrag")) return;
        HandleDragOver(e);
    }

    private void OnFileListViewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("FM.App.TabDrag")) return;
        HandleDragOver(e);
    }

    private void HandleDragOver(DragEventArgs e)
    {
        if (e.Data.GetDataPresent("FM.App.TabDrag")) return;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || ViewModel == null)
        {
            e.Effects = DragDropEffects.None;
            ClearDropHighlights();
            e.Handled = true;
            return;
        }

        // Auto-scroll when near top or bottom edges (~24 px)
        var scrollViewer = FileListView.FindVisualChild<ScrollViewer>();
        if (scrollViewer != null)
        {
            Point pos = e.GetPosition(FileListView);
            if (pos.Y >= 0 && pos.Y < 24)
            {
                scrollViewer.LineUp();
            }
            else if (pos.Y <= FileListView.ActualHeight && pos.Y > FileListView.ActualHeight - 24)
            {
                scrollViewer.LineDown();
            }
        }

        string[]? sources = null;
        try
        {
            sources = e.Data.GetData(DataFormats.FileDrop) as string[];
        }
        catch
        {
            // Ignore format extraction errors
        }

        if (sources == null || sources.Length == 0)
        {
            e.Effects = DragDropEffects.None;
            ClearDropHighlights();
            e.Handled = true;
            return;
        }

        // Target: hovered directory item or pane CurrentPath
        var depObj = e.OriginalSource as DependencyObject;
        var hoverItem = depObj?.FindVisualAncestor<ListViewItem>();

        string targetDir;
        bool isHoveringFolder = false;

        if (hoverItem?.DataContext is FileItemViewModel fileItem &&
            fileItem.IsDirectory &&
            !sources.Contains(fileItem.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            targetDir = fileItem.FullPath;
            isHoveringFolder = true;
        }
        else
        {
            targetDir = ViewModel.CurrentPath;
        }

        var resolution = DropEffectResolver.Resolve(ViewModel.FileSystem, sources, targetDir, e.KeyStates);
        if (resolution.IsValid)
        {
            e.Effects = resolution.Effect;
            ApplyDropHighlight(isHoveringFolder ? hoverItem : null);
        }
        else
        {
            e.Effects = DragDropEffects.None;
            ClearDropHighlights();
        }

        e.Handled = true;
    }

    private void OnFileListViewDragLeave(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("FM.App.TabDrag")) return;
        ClearDropHighlights();
    }

    private void OnFileListViewDrop(object sender, DragEventArgs e)
    {
        ClearDropHighlights();

        if (e.Data.GetDataPresent("FM.App.TabDrag")) return;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || ViewModel == null)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        string[]? sources = null;
        try
        {
            sources = e.Data.GetData(DataFormats.FileDrop) as string[];
        }
        catch
        {
            // Ignore format extraction errors
        }

        if (sources == null || sources.Length == 0)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var depObj = e.OriginalSource as DependencyObject;
        var hoverItem = depObj?.FindVisualAncestor<ListViewItem>();

        string targetDir;
        if (hoverItem?.DataContext is FileItemViewModel fileItem &&
            fileItem.IsDirectory &&
            !sources.Contains(fileItem.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            targetDir = fileItem.FullPath;
        }
        else
        {
            targetDir = ViewModel.CurrentPath;
        }

        var resolution = DropEffectResolver.Resolve(ViewModel.FileSystem, sources, targetDir, e.KeyStates);
        if (!resolution.IsValid || !resolution.Kind.HasValue)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var mainVm = Window.GetWindow(this)?.DataContext as MainViewModel;
        if (mainVm != null)
        {
            LastDropWasInternal = true;
            // OwnerPane for Undo:
            // Internal drag -> Source Pane
            // External drag (from Explorer) -> Target Pane (this ViewModel)
            FilePaneViewModel ownerPane = ViewModel;
            if (e.Data.GetDataPresent("FM.App.PaneDrag"))
            {
                var paneId = e.Data.GetData("FM.App.PaneDrag") as string;
                if (int.TryParse(paneId, out int srcIdx) && srcIdx >= 0 && srcIdx < mainVm.Panes.Count)
                {
                    ownerPane = mainVm.Panes[srcIdx];
                }
                else if (paneId == "Left") ownerPane = mainVm.LeftPane;
                else if (paneId == "Right") ownerPane = mainVm.RightPane;
            }

            // Fast enqueue - returns immediately without blocking OLE loop
            mainVm.EnqueueTransfer(resolution.Kind.Value, sources, targetDir, ownerPane);

            // Activate target pane where files landed
            ViewModel.NotifyActivated();
        }

        e.Effects = resolution.Effect;
        e.Handled = true;
    }

    private void ApplyDropHighlight(ListViewItem? hoverItem)
    {
        if (hoverItem != null)
        {
            if (_lastHoverItem != hoverItem)
            {
                ClearDropHighlights();
                _lastHoverItem = hoverItem;
                _lastHoverOriginalBackground = hoverItem.Background;
                hoverItem.Background = ItemHoverHighlightBrush;
            }
        }
        else
        {
            ClearDropHighlights();
            PaneBorder.BorderBrush = HighlightBrush;
        }
    }

    private void ClearDropHighlights()
    {
        if (_lastHoverItem != null)
        {
            _lastHoverItem.Background = _lastHoverOriginalBackground;
            _lastHoverItem = null;
            _lastHoverOriginalBackground = null;
        }

        // Restore PaneBorder brush according to active state by clearing local value
        PaneBorder.ClearValue(Border.BorderBrushProperty);
    }

    #region Tab Dragging and Edge Docking

    private Point? _tabDragStartPoint;
    private bool _isTabDragging;
    private DockEdge _currentDockEdge = DockEdge.None;

    private void OnTabPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep && dep.FindVisualAncestor<Button>() != null)
        {
            return;
        }
        _tabDragStartPoint = e.GetPosition(this);
    }

    private void OnTabPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isTabDragging || _tabDragStartPoint == null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point currentPoint = e.GetPosition(this);
        Vector diff = _tabDragStartPoint.Value - currentPoint;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            StartTabDrag();
        }
    }

    private void OnTabPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _tabDragStartPoint = null;
    }

    private void StartTabDrag()
    {
        if (ViewModel == null) return;
        _isTabDragging = true;

        Window? window = Window.GetWindow(this);
        UIElement? rootElement = window?.Content as UIElement ?? window;
        AdornerLayer? layer = rootElement != null ? AdornerLayer.GetAdornerLayer(rootElement) : null;

        if (layer != null && rootElement != null)
        {
            var ghostBorder = CreateTabDragVisual();
            _currentTabAdorner = new DragAdorner(rootElement, ghostBorder);
            layer.Add(_currentTabAdorner);
            _currentTabAdorner.UpdatePosition(10, 10);
            DragDrop.AddGiveFeedbackHandler(TabCapsuleBorder, OnTabGiveFeedback);
        }

        // Tactile "picked up" visual effect on the original tab capsule
        TabCapsuleBorder.Opacity = 0.35;

        try
        {
            var data = new DataObject();
            data.SetData("FM.App.TabDrag", ViewModel);
            DragDrop.DoDragDrop(TabCapsuleBorder, data, DragDropEffects.Move);
        }
        finally
        {
            _isTabDragging = false;
            _tabDragStartPoint = null;
            TabCapsuleBorder.Opacity = 1.0;

            if (_currentTabAdorner != null)
            {
                DragDrop.RemoveGiveFeedbackHandler(TabCapsuleBorder, OnTabGiveFeedback);
                layer?.Remove(_currentTabAdorner);
                _currentTabAdorner = null;
            }

            // Ensure any active dock preview overlays are closed across all panes
            var mainVm = window?.DataContext as MainViewModel;
            if (mainVm != null)
            {
                var workspace = window?.FindVisualChild<MultiPaneWorkspace>();
                if (workspace != null)
                {
                    foreach (var pane in mainVm.Panes)
                    {
                        var view = workspace.GetViewForPane(pane);
                        view?.HideDockPreview();
                    }
                }
            }
        }
    }

    private void OnTabGiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        _currentTabAdorner?.UpdatePosition(10, 10);
        e.UseDefaultCursors = true;
        e.Handled = true;
    }

    private void OnFileGiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        _currentFileAdorner?.UpdatePosition(12, 12);
        e.UseDefaultCursors = true;
        e.Handled = true;
    }

    private Border CreateTabDragVisual()
    {
        var border = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0x26, 0x62)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4, 10, 4),
            IsHitTestVisible = false,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Direction = 270,
                ShadowDepth = 5,
                BlurRadius = 14,
                Opacity = 0.28
            }
        };

        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        // Rose folder icon
        sp.Children.Add(new TextBlock
        {
            Text = "📁",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0x26, 0x62)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });

        // Folder name
        sp.Children.Add(new TextBlock
        {
            Text = ViewModel?.CurrentFolderName ?? "Folder",
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37)),
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 180,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        // Close icon
        sp.Children.Add(new TextBlock
        {
            Text = "✕",
            FontSize = 9,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        });

        border.Child = sp;

        // Pop-up lift animation: subtle scale + slight rotation
        var tg = new TransformGroup();
        var scale = new ScaleTransform(0.92, 0.92);
        var rotate = new RotateTransform(-2.5);
        tg.Children.Add(scale);
        tg.Children.Add(rotate);
        border.RenderTransform = tg;
        border.RenderTransformOrigin = new Point(0.5, 0.5);

        var anim = new DoubleAnimation(0.92, 1.05, new Duration(TimeSpan.FromMilliseconds(130)))
        {
            EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut }
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);

        return border;
    }

    private Border CreateFileDragVisual(string[] selected)
    {
        var border = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 12, 6),
            IsHitTestVisible = false,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Direction = 270,
                ShadowDepth = 5,
                BlurRadius = 14,
                Opacity = 0.25
            }
        };

        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        string firstName = Path.GetFileName(selected[0]);
        bool isDir = Directory.Exists(selected[0]);

        // File/Folder icon
        sp.Children.Add(new TextBlock
        {
            Text = isDir ? "📁" : "📄",
            FontSize = 13,
            Foreground = isDir
                ? new SolidColorBrush(Color.FromRgb(0xD9, 0x26, 0x62))
                : new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        // Item name
        sp.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(firstName) ? selected[0] : firstName,
            FontWeight = FontWeights.Medium,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37)),
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 180,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        // Count badge if > 1 item
        if (selected.Length > 1)
        {
            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xD9, 0x26, 0x62)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = new TextBlock
            {
                Text = $"+{selected.Length - 1}",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            sp.Children.Add(badge);
        }

        border.Child = sp;

        var tg = new TransformGroup();
        var scale = new ScaleTransform(0.92, 0.92);
        var rotate = new RotateTransform(-2.0);
        tg.Children.Add(scale);
        tg.Children.Add(rotate);
        border.RenderTransform = tg;
        border.RenderTransformOrigin = new Point(0.5, 0.5);

        var anim = new DoubleAnimation(0.92, 1.04, new Duration(TimeSpan.FromMilliseconds(120)))
        {
            EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut }
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);

        return border;
    }

    private void OnPaneDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("FM.App.TabDrag"))
        {
            UpdateDockEdge(e);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void OnPaneDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("FM.App.TabDrag"))
        {
            UpdateDockEdge(e);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void UpdateDockEdge(DragEventArgs e)
    {
        var draggedVm = e.Data.GetData("FM.App.TabDrag") as FilePaneViewModel;
        if (draggedVm != null && draggedVm != ViewModel)
        {
            var pos = e.GetPosition(this);
            double w = Math.Max(1, ActualWidth);
            double h = Math.Max(1, ActualHeight);
            double relX = Math.Clamp(pos.X / w, 0.0, 1.0);
            double relY = Math.Clamp(pos.Y / h, 0.0, 1.0);

            // Edge docking zone: outer 25% of pane width/height.
            // Center 50% zone is Swap zone (everywhere inside pane is responsive).
            const double threshold = 0.25;

            double distLeft = relX;
            double distRight = 1.0 - relX;
            double distTop = relY;
            double distBottom = 1.0 - relY;

            double minDist = Math.Min(Math.Min(distLeft, distRight), Math.Min(distTop, distBottom));

            DockEdge newEdge;
            if (minDist <= threshold)
            {
                if (minDist == distLeft)
                    newEdge = DockEdge.Left;
                else if (minDist == distRight)
                    newEdge = DockEdge.Right;
                else if (minDist == distTop)
                    newEdge = DockEdge.Top;
                else
                    newEdge = DockEdge.Bottom;
            }
            else
            {
                newEdge = DockEdge.Center;
            }

            if (newEdge != _currentDockEdge)
            {
                _currentDockEdge = newEdge;
                ShowDockPreview(newEdge);
            }
        }
        else
        {
            if (_currentDockEdge != DockEdge.None)
            {
                HideDockPreview();
            }
        }
    }

    private void ShowDockPreview(DockEdge edge)
    {
        double halfW = ActualWidth * 0.5;
        double halfH = ActualHeight * 0.5;

        switch (edge)
        {
            case DockEdge.Right:
                DockPreviewOverlay.HorizontalAlignment = HorizontalAlignment.Right;
                DockPreviewOverlay.VerticalAlignment = VerticalAlignment.Stretch;
                DockPreviewOverlay.Width = halfW;
                DockPreviewOverlay.Height = double.NaN;
                DockPreviewBadge.Visibility = Visibility.Collapsed;
                break;
            case DockEdge.Bottom:
                DockPreviewOverlay.HorizontalAlignment = HorizontalAlignment.Stretch;
                DockPreviewOverlay.VerticalAlignment = VerticalAlignment.Bottom;
                DockPreviewOverlay.Height = halfH;
                DockPreviewOverlay.Width = double.NaN;
                DockPreviewBadge.Visibility = Visibility.Collapsed;
                break;
            case DockEdge.Left:
                DockPreviewOverlay.HorizontalAlignment = HorizontalAlignment.Left;
                DockPreviewOverlay.VerticalAlignment = VerticalAlignment.Stretch;
                DockPreviewOverlay.Width = halfW;
                DockPreviewOverlay.Height = double.NaN;
                DockPreviewBadge.Visibility = Visibility.Collapsed;
                break;
            case DockEdge.Top:
                DockPreviewOverlay.HorizontalAlignment = HorizontalAlignment.Stretch;
                DockPreviewOverlay.VerticalAlignment = VerticalAlignment.Top;
                DockPreviewOverlay.Height = halfH;
                DockPreviewOverlay.Width = double.NaN;
                DockPreviewBadge.Visibility = Visibility.Collapsed;
                break;
            case DockEdge.Center:
                DockPreviewOverlay.HorizontalAlignment = HorizontalAlignment.Stretch;
                DockPreviewOverlay.VerticalAlignment = VerticalAlignment.Stretch;
                DockPreviewOverlay.Width = double.NaN;
                DockPreviewOverlay.Height = double.NaN;
                DockPreviewBadge.Visibility = Visibility.Visible;
                break;
            default:
                HideDockPreview();
                return;
        }

        // Direct solid display without asynchronous animations to avoid OLE loop flicker
        DockPreviewOverlay.Opacity = 1.0;
        DockPreviewOverlay.Visibility = Visibility.Visible;
    }

    internal void HideDockPreview()
    {
        _currentDockEdge = DockEdge.None;
        DockPreviewBadge.Visibility = Visibility.Collapsed;
        DockPreviewOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnPaneDragLeave(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("FM.App.TabDrag"))
        {
            // Only hide if the cursor has actually left the physical boundaries of this pane
            var pos = e.GetPosition(this);
            if (pos.X < 0 || pos.X > ActualWidth || pos.Y < 0 || pos.Y > ActualHeight)
            {
                HideDockPreview();
            }
        }
    }

    private void OnPaneDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("FM.App.TabDrag"))
        {
            var edge = _currentDockEdge;
            HideDockPreview();

            if (ViewModel != null)
            {
                var draggedVm = e.Data.GetData("FM.App.TabDrag") as FilePaneViewModel;
                if (draggedVm != null && draggedVm != ViewModel)
                {
                    // Fallback to Center (Swap) if edge was None so dropping ANYWHERE in pane works
                    if (edge == DockEdge.None)
                    {
                        edge = DockEdge.Center;
                    }

                    var mainVm = Window.GetWindow(this)?.DataContext as MainViewModel;
                    mainVm?.DockPane(draggedVm, ViewModel, edge);
                    e.Effects = DragDropEffects.Move;
                    e.Handled = true;
                }
            }
        }
    }

    #endregion
}