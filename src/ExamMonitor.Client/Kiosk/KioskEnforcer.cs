// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Client\Kiosk\KioskEnforcer.cs
using System.Windows;
using System.Windows.Input;

namespace ExamMonitor.Client.Kiosk;

public static class KioskEnforcer
{
    public static void ApplyFullscreenKiosk(Window window, bool blockAltF4 = true)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.WindowState = WindowState.Maximized;
        window.Topmost = true;

        if (blockAltF4)
        {
            window.PreviewKeyDown += OnPreviewKeyDownBlockSystemClose;
        }
    }

    public static void ReleaseFullscreenKiosk(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.PreviewKeyDown -= OnPreviewKeyDownBlockSystemClose;
        window.Topmost = false;
        window.WindowStyle = WindowStyle.SingleBorderWindow;
        window.ResizeMode = ResizeMode.CanResize;
        window.WindowState = WindowState.Normal;
    }

    private static void OnPreviewKeyDownBlockSystemClose(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.System && e.SystemKey == Key.F4)
        {
            e.Handled = true;
        }
    }
}
