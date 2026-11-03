using System.Windows;
using FM.App.ViewModels;

namespace FM.App.Views;

public partial class MultiRenameDialog : Window
{
    public MultiRenameDialog(MultiRenameViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.RequestClose += success =>
        {
            DialogResult = success;
            Close();
        };
    }
}

