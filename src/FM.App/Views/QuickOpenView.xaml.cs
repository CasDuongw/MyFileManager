using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FM.App.Services.QuickOpen;
using FM.App.ViewModels;

namespace FM.App.Views;

public partial class QuickOpenView : UserControl
{
    public QuickOpenView()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private QuickOpenViewModel? ViewModel => DataContext as QuickOpenViewModel;

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (Visibility == Visibility.Visible)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void OnScrimMouseDown(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.Close();
    }

    private void OnSearchBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel == null) return;

        switch (e.Key)
        {
            case Key.Down:
                ViewModel.MoveSelection(1);
                ScrollSelectedIntoView();
                e.Handled = true;
                break;

            case Key.Up:
                ViewModel.MoveSelection(-1);
                ScrollSelectedIntoView();
                e.Handled = true;
                break;

            case Key.Enter:
                ViewModel.ExecuteSelected();
                e.Handled = true;
                break;

            case Key.Escape:
                ViewModel.Close();
                e.Handled = true;
                break;
        }
    }

    private void OnResultsListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ScrollSelectedIntoView();
    }

    private void OnResultsListItemClicked(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.ExecuteSelected();
    }

    private void ScrollSelectedIntoView()
    {
        if (ResultsList.SelectedItem != null)
        {
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
        }
    }
}
