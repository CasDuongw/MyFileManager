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
    private bool _isSyncingSelection;

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
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }
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
}
