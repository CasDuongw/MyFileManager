using System.Diagnostics;
using System.IO;
using System.Windows;

namespace FM.App.Views;

public partial class CrashReportDialog : Window
{
    private readonly Exception _exception;
    private readonly string _logDirectory;

    public bool ShouldRestart { get; private set; }
    public bool ShouldContinue { get; private set; }

    public CrashReportDialog(Exception exception, string logDirectory, bool canContinue = true)
    {
        InitializeComponent();
        _exception = exception;
        _logDirectory = logDirectory;

        ExceptionMessageTextBlock.Text = $"{_exception.GetType().Name}: {_exception.Message}";
        LogPathTextBlock.Text = $"Thư mục Log: {_logDirectory}";
        StackTraceTextBox.Text = $"{_exception.GetType().FullName}: {_exception.Message}\n\n{_exception.StackTrace}";

        if (!canContinue)
        {
            ContinueButton.Visibility = Visibility.Collapsed;
        }
    }

    private void OnOpenLogsFolderClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Directory.Exists(_logDirectory))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _logDirectory,
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }

    private void OnCopyErrorClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string errorDetails = $"Exception: {_exception.GetType().FullName}\nMessage: {_exception.Message}\n\nStack Trace:\n{_exception.StackTrace}";
            Clipboard.SetText(errorDetails);
            MessageBox.Show(this, "Đã sao chép chi tiết lỗi vào Clipboard.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch { }
    }

    private void OnContinueClicked(object sender, RoutedEventArgs e)
    {
        ShouldContinue = true;
        DialogResult = true;
        Close();
    }

    private void OnRestartClicked(object sender, RoutedEventArgs e)
    {
        ShouldRestart = true;
        DialogResult = true;
        Close();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

