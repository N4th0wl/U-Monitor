// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Server\Services\FrameDispatcherService.cs
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ExamMonitor.Common.Models;

namespace ExamMonitor.Server.Services;

public sealed class DecodedFrameEventArgs : EventArgs
{
    public required string ClientId { get; init; }
    public required BitmapSource Image { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required bool IsHighResolution { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

public sealed class FrameDispatcherService
{
    private readonly Dispatcher? _uiDispatcher;

    public FrameDispatcherService(Dispatcher? uiDispatcher = null)
    {
        _uiDispatcher = uiDispatcher ?? Application.Current?.Dispatcher;
    }

    public event EventHandler<DecodedFrameEventArgs>? FrameDecoded;

    public void EnqueueFrame(FramePacket packet)
    {
        if (packet.JpegPayload is null || packet.JpegPayload.Length == 0)
        {
            return;
        }

        BitmapSource? frozenBitmap = TryDecodeJpeg(packet.JpegPayload);
        if (frozenBitmap is null)
        {
            return;
        }

        var args = new DecodedFrameEventArgs
        {
            ClientId = packet.ClientId,
            Image = frozenBitmap,
            Width = packet.Width > 0 ? packet.Width : frozenBitmap.PixelWidth,
            Height = packet.Height > 0 ? packet.Height : frozenBitmap.PixelHeight,
            IsHighResolution = packet.IsHighResolution,
            Timestamp = packet.Timestamp
        };

        if (_uiDispatcher is not null && !_uiDispatcher.CheckAccess())
        {
            _uiDispatcher.BeginInvoke(DispatcherPriority.Render, () => FrameDecoded?.Invoke(this, args));
        }
        else
        {
            FrameDecoded?.Invoke(this, args);
        }
    }

    public static BitmapSource? TryDecodeJpeg(byte[] jpegBytes)
    {
        try
        {
            using var ms = new MemoryStream(jpegBytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze(); // Cross-thread safe for WPF UI binding
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
