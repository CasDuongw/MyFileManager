using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FM.App.Helpers;
using FM.App.ViewModels;

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

    private static readonly Brush HighlightBrush = CreateFrozenBrush(Color.FromRgb(0x00, 0x78, 0xD4));
    private static readonly Brush ItemHoverHighlightBrush = CreateFrozenBrush(Color.FromArgb(0x40, 0x00, 0x78, 0xD4));
    private static readonly Brush InactivePaneBorderBrush = CreateFrozenBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));

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
            if (FileListView.SelectedItem is FileItemViewModel item)
            {
                ViewModel?.OpenItemCommand.Execute(item);
                e.Handled = true;
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
        HandleDragOver(e);
    }

    private void OnFileListViewDragOver(object sender, DragEventArgs e)
    {
        HandleDragOver(e);
    }

    private void HandleDragOver(DragEventArgs e)
    {
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
        ClearDropHighlights();
    }

    private void OnFileListViewDrop(object sender, DragEventArgs e)
    {
        ClearDropHighlights();

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

        // Restore PaneBorder brush according to active state
        PaneBorder.BorderBrush = (ViewModel?.IsActive == true)
            ? HighlightBrush
            : InactivePaneBorderBrush;
    }
}
