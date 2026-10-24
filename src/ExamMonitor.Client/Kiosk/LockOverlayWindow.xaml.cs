// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Client\Kiosk\LockOverlayWindow.xaml.cs
using System.ComponentModel;
using System.Windows;

namespace ExamMonitor.Client.Kiosk;

public partial class LockOverlayWindow : Window
{
    private bool _allowClose;

    public LockOverlayWindow()
    {
        InitializeComponent();
        KioskEnforcer.ApplyFullscreenKiosk(this, blockAltF4: true);
    }

    public void SetLockMessage(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            LockReasonTextBlock.Text = message;
        }
    }

    public void UnlockAndClose()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }
}
