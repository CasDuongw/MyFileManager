using System.Windows;
using FM.Core.Operations.Transfer;

namespace FM.App.Views;

/// <summary>
/// Interaction logic for FailureDialog.xaml.
/// Displays file error information, attempt counter, and options to Retry, Skip, or Abort.
/// </summary>
public partial class FailureDialog : Window
{
    public FailureDecision Decision { get; private set; } = new(FailureAction.Abort);

    public FailureDialog(FailureInfo info)
    {
        InitializeComponent();

        AttemptText.Text = $"Attempt #{info.Attempt}";
        FilePathText.Text = info.Path;
        FilePathText.ToolTip = info.Path;
        ErrorMessageText.Text = info.Error?.Message ?? "An unknown error occurred.";

        Loaded += (s, e) => RetryButton.Focus();
    }

    private void OnRetryClicked(object sender, RoutedEventArgs e)
    {
        Decision = new FailureDecision(FailureAction.Retry, ApplyToAllCheckBox.IsChecked == true);
        Close();
    }

    private void OnSkipClicked(object sender, RoutedEventArgs e)
    {
        Decision = new FailureDecision(FailureAction.Skip, ApplyToAllCheckBox.IsChecked == true);
        Close();
    }

    private void OnAbortClicked(object sender, RoutedEventArgs e)
    {
        Decision = new FailureDecision(FailureAction.Abort, ApplyToAllCheckBox.IsChecked == true);
        Close();
    }
}
