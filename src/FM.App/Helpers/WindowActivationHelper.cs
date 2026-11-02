using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FM.App.Helpers;

public static class WindowActivationHelper
{
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public static void BringToFront(Window window)
    {
        if (window == null) return;

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Show();
        window.Activate();
        window.Focus();

        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            ShowWindow(handle, SW_RESTORE);
            SetForegroundWindow(handle);
        }
    }
}
