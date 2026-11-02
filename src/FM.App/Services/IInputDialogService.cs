using System.Windows;

namespace FM.App.Services;

/// <summary>
/// Abstraction for prompting modal user input.
/// </summary>
public interface IInputDialogService
{
    string? Prompt(string title, string message, string initialValue = "", int? selectionLength = null);
    bool Confirm(string title, string message);
}

public class InputDialogService : IInputDialogService
{
    public string? Prompt(string title, string message, string initialValue = "", int? selectionLength = null)
    {
        var owner = Application.Current?.MainWindow;
        var dialog = new Views.InputDialog(title, message, initialValue, selectionLength)
        {
            Owner = owner
        };

        bool? result = dialog.ShowDialog();
        return (result == true) ? dialog.InputValue : null;
    }

    public bool Confirm(string title, string message)
    {
        var owner = Application.Current?.MainWindow;
        var res = MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return res == MessageBoxResult.Yes;
    }
}

