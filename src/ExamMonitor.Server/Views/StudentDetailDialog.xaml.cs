// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Server\Views\StudentDetailDialog.xaml.cs
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ExamMonitor.Server.ViewModels;

namespace ExamMonitor.Server.Views;

public partial class StudentDetailDialog : Window
{
    private readonly StudentDetailViewModel _viewModel;
    private DateTimeOffset _lastMouseMoveSent = DateTimeOffset.MinValue;

    public StudentDetailDialog(StudentDetailViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.StartHighResInspectionAsync();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        await _viewModel.StopHighResInspectionAsync();
        base.OnClosing(e);
    }

    private async void OnRemoteImageMouseMove(object sender, MouseEventArgs e)
    {
        if (!_viewModel.IsRemoteControlEnabled)
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if ((now - _lastMouseMoveSent).TotalMilliseconds < 40)
        {
            return;
        }
        _lastMouseMoveSent = now;

        if (TryGetNormalizedCoords(e, out double nx, out double ny))
        {
            await _viewModel.SendRemoteMouseAsync(nx, ny, mouseButton: 0);
        }
    }

    private async void OnRemoteImageMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_viewModel.IsRemoteControlEnabled)
        {
            return;
        }

        if (TryGetNormalizedCoords(e, out double nx, out double ny))
        {
            int btn = e.ChangedButton == MouseButton.Right ? 3 : 1;
            await _viewModel.SendRemoteMouseAsync(nx, ny, btn);
        }
    }

    private async void OnRemoteImageMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_viewModel.IsRemoteControlEnabled)
        {
            return;
        }

        if (TryGetNormalizedCoords(e, out double nx, out double ny))
        {
            int btn = e.ChangedButton == MouseButton.Right ? 4 : 2;
            await _viewModel.SendRemoteMouseAsync(nx, ny, btn);
        }
    }

    private async void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_viewModel.IsRemoteControlEnabled || DirectMessageTextBox.IsKeyboardFocused)
        {
            return;
        }

        int vk = KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key);
        if (vk > 0)
        {
            await _viewModel.SendRemoteKeyAsync(vk, isKeyUp: false);
            e.Handled = true;
        }
    }

    private async void OnWindowPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!_viewModel.IsRemoteControlEnabled || DirectMessageTextBox.IsKeyboardFocused)
        {
            return;
        }

        int vk = KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key);
        if (vk > 0)
        {
            await _viewModel.SendRemoteKeyAsync(vk, isKeyUp: true);
            e.Handled = true;
        }
    }

    private bool TryGetNormalizedCoords(MouseEventArgs e, out double nx, out double ny)
    {
        nx = 0;
        ny = 0;
        double w = RemoteDesktopImage.ActualWidth;
        double h = RemoteDesktopImage.ActualHeight;
        if (w <= 1 || h <= 1)
        {
            return false;
        }

        Point pt = e.GetPosition(RemoteDesktopImage);
        nx = Math.Clamp(pt.X / w, 0.0, 1.0);
        ny = Math.Clamp(pt.Y / h, 0.0, 1.0);
        return true;
    }

    private void OnSaveScreenshotClicked(object sender, RoutedEventArgs e)
    {
        BitmapSource? frame = _viewModel.Student.ThumbnailFrame;
        if (frame is null)
        {
            return;
        }

        try
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Screenshots");
            Directory.CreateDirectory(dir);
            string fileName = $"{_viewModel.Student.StudentId}_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
            string fullPath = Path.Combine(dir, fileName);

            var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
            encoder.Frames.Add(BitmapFrame.Create(frame));
            using FileStream fs = File.Create(fullPath);
            encoder.Save(fs);

            MessageBox.Show(this, $"Screenshot disimpan ke:\n{fullPath}", "Screenshot Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Gagal menyimpan screenshot: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
