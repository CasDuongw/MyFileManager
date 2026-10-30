using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using FM.App.Services;
using FM.App.ViewModels;
using FM.Core.Operations.Transfer;

namespace FM.App.Views;

/// <summary>
/// Interaction logic for ConflictDialog.xaml.
/// Uses Windows 11 Fluent layout, DWM rounded window corners, dynamic backup safety notices,
/// and asynchronous STA thumbnail rendering.
/// </summary>
public partial class ConflictDialog : Window
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public ConflictDialogViewModel ViewModel { get; }

    public ConflictDecision Decision => ViewModel.Decision;

    public ConflictDialog(ConflictInfo info, IThumbnailService? thumbnailService = null)
    {
        InitializeComponent();

        ViewModel = new ConflictDialogViewModel(info, thumbnailService);
        DataContext = ViewModel;

        ViewModel.RequestClose += () =>
        {
            Dispatcher.Invoke(() =>
            {
                if (IsLoaded)
                {
                    Close();
                }
            });
        };

        SourceInitialized += (s, e) =>
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int preference = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
            }
            catch
            {
                // Silently ignore on Windows 10 or when DWM attribute is unsupported
            }
        };

        Loaded += (s, e) => KeepBothButton.Focus();

        Closing += (s, e) => ViewModel.CancelThumbnailLoading();
    }

    private void OnKeepBothClicked(object sender, RoutedEventArgs e) => ViewModel.ChooseKeepBoth();
    private void OnOverwriteIfNewerClicked(object sender, RoutedEventArgs e) => ViewModel.ChooseOverwriteIfNewer();
    private void OnOverwriteClicked(object sender, RoutedEventArgs e) => ViewModel.ChooseOverwrite();
    private void OnSkipClicked(object sender, RoutedEventArgs e) => ViewModel.ChooseSkip();
    private void OnCancelClicked(object sender, RoutedEventArgs e) => ViewModel.ChooseCancel();
}
