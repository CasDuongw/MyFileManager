using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    public FilePaneView()
    {
        InitializeComponent();
    }

    public void FocusAddressBar()
    {
        AddressTextBox.Focus();
        AddressTextBox.SelectAll();
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
            e.Handled = true;
        }
    }

    private void OnGridViewColumnHeaderClicked(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is GridViewColumnHeader header && header.Tag is string columnTag)
        {
            ViewModel?.SortCommand.Execute(columnTag);
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
}
