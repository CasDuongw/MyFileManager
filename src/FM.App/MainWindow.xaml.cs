using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        ViewModel?.InitializeSettings();
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
        // 1. Tab: Switch active pane
        if (e.Key == Key.Tab && (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift))
        {
            // If user is currently editing the address TextBox, let them tab out or switch pane?
            // In standard dual-pane file managers, Tab switches active pane
            ViewModel?.SwitchActivePane();
            FocusActivePane();
            e.Handled = true;
            return;
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
            ViewModel?.ActivePane.Refresh();
            e.Handled = true;
            return;
        }

        // 4. Alt+Left: Back in active pane
        bool isAlt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
        Key actualKey = e.Key == Key.System ? e.SystemKey : e.Key;

        if (isAlt && actualKey == Key.Left)
        {
            if (ViewModel?.ActivePane.GoBackCommand.CanExecute(null) == true)
            {
                ViewModel.ActivePane.GoBackCommand.Execute(null);
                e.Handled = true;
            }
        }
        // 5. Alt+Right: Forward in active pane
        else if (isAlt && actualKey == Key.Right)
        {
            if (ViewModel?.ActivePane.GoForwardCommand.CanExecute(null) == true)
            {
                ViewModel.ActivePane.GoForwardCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void FocusActivePane()
    {
        if (ViewModel == null) return;

        if (ViewModel.ActivePane == ViewModel.LeftPane)
        {
            LeftPaneControl.FocusFileList();
        }
        else
        {
            RightPaneControl.FocusFileList();
        }
    }

    private void FocusActiveAddressBar()
    {
        if (ViewModel == null) return;

        if (ViewModel.ActivePane == ViewModel.LeftPane)
        {
            LeftPaneControl.FocusAddressBar();
        }
        else
        {
            RightPaneControl.FocusAddressBar();
        }
    }
}