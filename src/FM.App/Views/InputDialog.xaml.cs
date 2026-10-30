using System.Windows;
using System.Windows.Input;

namespace FM.App.Views;

/// <summary>
/// Interaction logic for InputDialog.xaml.
/// Small, clean modal input dialog centered on owner window.
/// </summary>
public partial class InputDialog : Window
{
    public string? InputValue { get; private set; }

    public InputDialog(string title, string message, string initialValue = "", int? selectionLength = null)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        InputTextBox.Text = initialValue;

        Loaded += (s, e) =>
        {
            InputTextBox.Focus();
            if (selectionLength.HasValue && selectionLength.Value <= initialValue.Length)
            {
                InputTextBox.Select(0, selectionLength.Value);
            }
            else
            {
                InputTextBox.SelectAll();
            }
        };
    }

    private void OnOkClicked(object sender, RoutedEventArgs e)
    {
        InputValue = InputTextBox.Text;
        DialogResult = true;
        Close();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnInputTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnOkClicked(sender, e);
            e.Handled = true;
        }
    }
}
