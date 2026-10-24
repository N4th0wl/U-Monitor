// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Client\Services\ScreenCaptureService.cs
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ExamMonitor.Common;
using ExamMonitor.Common.Models;

namespace ExamMonitor.Client.Services;

public sealed class ScreenCaptureService
{
    private const int SRCCOPY = 0x00CC0020;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int CURSOR_SHOWING = 0x00000001;
    private const int DI_NORMAL = 0x0003;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorInfo(ref CURSORINFO pci);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyWidth, int istepIfAniCur, IntPtr hbrFlickerFreeDraw, int diFlags);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    public FramePacket CaptureFrame(string clientId, bool highResolution)
    {
        int screenWidth = Math.Max(GetSystemMetrics(SM_CXSCREEN), 1280);
        int screenHeight = Math.Max(GetSystemMetrics(SM_CYSCREEN), 720);

        int targetWidth = highResolution ? screenWidth : ProtocolConstants.DefaultThumbnailWidth;
        int targetHeight = highResolution ? screenHeight : ProtocolConstants.DefaultThumbnailHeight;
        int jpegQuality = highResolution ? ProtocolConstants.HighResJpegQuality : ProtocolConstants.DefaultThumbnailJpegQuality;

        BitmapSource bitmapSource = CaptureDesktopBitmap(screenWidth, screenHeight, targetWidth, targetHeight);
        byte[] jpegBytes = EncodeToJpeg(bitmapSource, jpegQuality);

        return new FramePacket
        {
            ClientId = clientId,
            Timestamp = DateTimeOffset.UtcNow,
            JpegPayload = jpegBytes,
            Width = bitmapSource.PixelWidth,
            Height = bitmapSource.PixelHeight,
            IsHighResolution = highResolution
        };
    }

    public static byte[] EncodeToJpeg(BitmapSource source, int qualityLevel)
    {
        var encoder = new JpegBitmapEncoder
        {
            QualityLevel = Math.Clamp(qualityLevel, 10, 100)
        };
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static BitmapSource CaptureDesktopBitmap(int screenWidth, int screenHeight, int targetWidth, int targetHeight)
    {
        IntPtr hDesktopWnd = GetDesktopWindow();
        IntPtr hDesktopDc = GetWindowDC(hDesktopWnd);
        IntPtr hMemoryDc = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr hOldBitmap = IntPtr.Zero;

        try
        {
            if (hDesktopDc != IntPtr.Zero)
            {
                hMemoryDc = CreateCompatibleDC(hDesktopDc);
                hBitmap = CreateCompatibleBitmap(hDesktopDc, screenWidth, screenHeight);
                if (hBitmap != IntPtr.Zero)
                {
                    hOldBitmap = SelectObject(hMemoryDc, hBitmap);
                    if (BitBlt(hMemoryDc, 0, 0, screenWidth, screenHeight, hDesktopDc, 0, 0, SRCCOPY))
                    {
                        // Draw live mouse cursor onto captured desktop frame (RustDesk-style)
                        var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
                        if (GetCursorInfo(ref ci) && (ci.flags & CURSOR_SHOWING) != 0 && ci.hCursor != IntPtr.Zero)
                        {
                            DrawIconEx(hMemoryDc, ci.ptScreenPos.x, ci.ptScreenPos.y, ci.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);
                        }

                        BitmapSource rawSource = Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());

                        if (targetWidth != screenWidth || targetHeight != screenHeight)
                        {
                            double scaleX = (double)targetWidth / screenWidth;
                            double scaleY = (double)targetHeight / screenHeight;
                            var scaled = new TransformedBitmap(rawSource, new ScaleTransform(scaleX, scaleY));
                            scaled.Freeze();
                            return scaled;
                        }

                        rawSource.Freeze();
                        return rawSource;
                    }
                }
            }
        }
        catch
        {
            // Fallback to synthetic frame below if running in non-interactive session
        }
        finally
        {
            if (hOldBitmap != IntPtr.Zero && hMemoryDc != IntPtr.Zero)
            {
                SelectObject(hMemoryDc, hOldBitmap);
            }
            if (hBitmap != IntPtr.Zero)
            {
                DeleteObject(hBitmap);
            }
            if (hMemoryDc != IntPtr.Zero)
            {
                DeleteDC(hMemoryDc);
            }
            if (hDesktopDc != IntPtr.Zero)
            {
                ReleaseDC(hDesktopWnd, hDesktopDc);
            }
        }

        return CreateSyntheticFrame(targetWidth, targetHeight);
    }

    public static BitmapSource CreateSyntheticFrame(int width, int height)
    {
        int stride = width * 3;
        byte[] pixels = new byte[height * stride];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * stride + x * 3;
                pixels[idx + 0] = (byte)((x * 255) / Math.Max(1, width));
                pixels[idx + 1] = (byte)((y * 255) / Math.Max(1, height));
                pixels[idx + 2] = 64;
            }
        }

        BitmapSource bmp = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Rgb24,
            null,
            pixels,
            stride);
        bmp.Freeze();
        return bmp;
    }
}
