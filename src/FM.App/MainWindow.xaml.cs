using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FM.App.Helpers;
using FM.App.ViewModels;

namespace FM.App;

/// <summary>
/// Interaction logic for MainWindow.xaml.
/// Handles pure view concerns: shortcut keys, focus switching between panes, and window lifecycle.
/// </summary>
public partial class MainWindow : Window
{
    public MainViewModel? ViewModel => DataContext as MainViewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        FocusActivePane();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (ViewModel != null)
        {
            double left = WindowState == WindowState.Normal ? Left : RestoreBounds.Left;
            double top = WindowState == WindowState.Normal ? Top : RestoreBounds.Top;
            double width = WindowState == WindowState.Normal ? Width : RestoreBounds.Width;
            double height = WindowState == WindowState.Normal ? Height : RestoreBounds.Height;
            ViewModel.PersistSettings(left, top, width, height, WindowState.ToString());
        }
    }

    private void OnSidebarSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is SidebarItemViewModel item)
        {
            ViewModel?.NavigateActivePane(item.Path);

            // Clear selection so the same item can be clicked repeatedly
            if (sender is ListBox listBox)
            {
                listBox.SelectedItem = null;
            }
        }
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 1. Tab: Switch active pane ONLY when focus is inside a file list
        // Inside the address bar Tab preserves normal tab navigation. Shift+Tab is never hijacked.
        if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None)
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            bool isInsideFileList = focused != null &&
                (focused.FindVisualAncestor<ListView>() != null || focused.FindVisualAncestor<ListViewItem>() != null);

            if (isInsideFileList)
            {
                ViewModel?.SwitchActivePane();
                FocusActivePane();
                e.Handled = true;
                return;
            }
        }

        // 2. Ctrl+L: Focus address bar in active pane
        if (e.Key == Key.L && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            FocusActiveAddressBar();
            e.Handled = true;
            return;
        }

        // 3. F5: Refresh active pane
        if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
        {
            ViewModel?.ActivePane?.Refresh();
            e.Handled = true;
            return;
        }

        // 4. Alt+Left: Back in active pane
        bool isAlt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
        Key actualKey = e.Key == Key.System ? e.SystemKey : e.Key;

        if (isAlt && actualKey == Key.Left)
        {
            if (ViewModel?.ActivePane?.GoBackCommand.CanExecute(null) == true)
            {
                ViewModel.ActivePane.GoBackCommand.Execute(null);
                e.Handled = true;
            }
        }
        // 5. Alt+Right: Forward in active pane
        else if (isAlt && actualKey == Key.Right)
        {
            if (ViewModel?.ActivePane?.GoForwardCommand.CanExecute(null) == true)
            {
                ViewModel.ActivePane.GoForwardCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        // 6. Ctrl+Shift+N: New Folder in active pane
        bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        // Ctrl+T: Open new tab
        if (isCtrl && !isShift && e.Key == Key.T)
        {
            ViewModel?.OpenNewTab();
            e.Handled = true;
            return;
        }

        // Ctrl+W: Close active tab
        if (isCtrl && !isShift && e.Key == Key.W)
        {
            if (ViewModel?.CanClosePanes == true)
            {
                ViewModel.ClosePaneCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        // Ctrl+Shift+F: Focus toolbar filter box
        if (isCtrl && isShift && e.Key == Key.F)
        {
            ToolbarFilterBox.Focus();
            ToolbarFilterBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (isCtrl && isShift && e.Key == Key.N)
        {
            if (ViewModel?.ActivePane?.NewFolderCommand.CanExecute(null) == true)
            {
                ViewModel.ActivePane.NewFolderCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        // 7. F2: Rename selected item in active pane
        if (e.Key == Key.F2 && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (ViewModel?.ActivePane?.RenameCommand.CanExecute(null) == true)
            {
                ViewModel.ActivePane.RenameCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        // 8. Ctrl+Z: Undo last operation in active pane
        if (isCtrl && !isShift && e.Key == Key.Z)
        {
            if (ViewModel?.ActivePane?.UndoCommand.CanExecute(null) == true)
            {
                ViewModel.ActivePane.UndoCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        // 9. Ctrl+Shift+C: Copy to other pane
        if (isCtrl && isShift && e.Key == Key.C)
        {
            if (ViewModel?.CopyToOtherPaneCommand.CanExecute(null) == true)
            {
                ViewModel.CopyToOtherPaneCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        // 10. Ctrl+Shift+M: Move to other pane
        if (isCtrl && isShift && e.Key == Key.M)
        {
            if (ViewModel?.MoveToOtherPaneCommand.CanExecute(null) == true)
            {
                ViewModel.MoveToOtherPaneCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        // 11. Ctrl+H: Toggle hidden files
        if (isCtrl && !isShift && e.Key == Key.H)
        {
            if (ViewModel?.ToggleHiddenFilesCommand.CanExecute(null) == true)
            {
                ViewModel.ToggleHiddenFilesCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        // 12. Layout Presets: Ctrl+Shift+1..4
        if (isCtrl && isShift)
        {
            if (e.Key == Key.D1 || e.Key == Key.NumPad1)
            {
                ViewModel?.SetLayoutMode(Models.PaneLayoutMode.Single);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.D2 || e.Key == Key.NumPad2)
            {
                ViewModel?.SetLayoutMode(Models.PaneLayoutMode.DualVertical);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.D3 || e.Key == Key.NumPad3)
            {
                ViewModel?.SetLayoutMode(Models.PaneLayoutMode.DualHorizontal);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.D4 || e.Key == Key.NumPad4)
            {
                ViewModel?.SetLayoutMode(Models.PaneLayoutMode.QuadGrid);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.T)
            {
                ViewModel?.SplitVerticalCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.W)
            {
                if (ViewModel?.CanClosePanes == true)
                {
                    ViewModel.ClosePaneCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
            }
        }

        // 13. Alt+1..4: Focus specific pane
        if (isAlt && !isCtrl && !isShift && ViewModel != null)
        {
            int paneIdx = -1;
            if (actualKey == Key.D1 || actualKey == Key.NumPad1) paneIdx = 0;
            else if (actualKey == Key.D2 || actualKey == Key.NumPad2) paneIdx = 1;
            else if (actualKey == Key.D3 || actualKey == Key.NumPad3) paneIdx = 2;
            else if (actualKey == Key.D4 || actualKey == Key.NumPad4) paneIdx = 3;

            if (paneIdx >= 0 && paneIdx < ViewModel.Panes.Count)
            {
                ViewModel.SetActivePane(ViewModel.Panes[paneIdx]);
                FocusActivePane();
                e.Handled = true;
                return;
            }
        }
    }

    private void FocusActivePane()
    {
        if (ViewModel?.ActivePane == null) return;
        var view = WorkspaceControl.GetViewForPane(ViewModel.ActivePane);
        view?.FocusFileList();
    }

    private void OnToolbarFilterBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ToolbarFilterBox.Text = string.Empty;
            FocusActivePaneList();
            e.Handled = true;
        }
    }

    public void FocusActivePaneList()
    {
        if (ViewModel?.ActivePane != null)
        {
            var activeView = WorkspaceControl.GetViewForPane(ViewModel.ActivePane);
            activeView?.FocusFileList();
        }
    }

    private void FocusActiveAddressBar()
    {
        if (ViewModel?.ActivePane == null) return;
        var view = WorkspaceControl.GetViewForPane(ViewModel.ActivePane);
        view?.FocusAddressBar();
    }
}