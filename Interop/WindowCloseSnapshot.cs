using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using unity_cli_ui.Services;

namespace unity_cli_ui.Interop;

internal sealed class WindowCloseSnapshot : IDisposable
{
    private const uint SourceCopyWithLayeredWindows = 0x40CC0020;
    private const uint NoActivate = 0x0010;

    private readonly Window _overlay;
    private readonly WindowRect _bounds;
    private readonly bool _topmost;

    private WindowCloseSnapshot(Window overlay, WindowRect bounds, bool topmost)
    {
        _overlay = overlay;
        _bounds = bounds;
        _topmost = topmost;
        _overlay.SourceInitialized += Overlay_SourceInitialized;
    }

    public static WindowCloseSnapshot? TryCreate(Window source)
    {
        var handle = new WindowInteropHelper(source).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var bounds))
        {
            return null;
        }

        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        var bitmap = Capture(bounds, width, height);
        if (bitmap is null)
        {
            return null;
        }

        var dpi = GetDpiForWindow(handle);
        var scale = dpi > 0 ? dpi / 96d : 1d;
        var overlay = new Window
        {
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Content = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Fill,
                SnapsToDevicePixels = true
            },
            Left = bounds.Left / scale,
            Top = bounds.Top / scale,
            Width = width / scale,
            Height = height / scale,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false,
            Topmost = source.Topmost,
            UseLayoutRounding = true,
            WindowStartupLocation = WindowStartupLocation.Manual,
            WindowStyle = WindowStyle.None
        };
        return new WindowCloseSnapshot(overlay, bounds, source.Topmost);
    }

    public void Show() => _overlay.Show();

    public async Task FadeOutAsync(
        TimeSpan duration,
        IEasingFunction easing,
        CancellationToken cancellationToken)
    {
        await WpfAnimationRunner.RunAsync(
            [(_overlay, UIElement.OpacityProperty, 0)],
            duration,
            easing,
            cancellationToken);
        _overlay.Opacity = 0;
    }

    public void Dispose()
    {
        _overlay.SourceInitialized -= Overlay_SourceInitialized;
        if (_overlay.IsLoaded)
        {
            _overlay.Close();
        }
    }

    private void Overlay_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(_overlay).Handle;
        var insertAfter = _topmost ? new IntPtr(-1) : IntPtr.Zero;
        SetWindowPos(
            handle,
            insertAfter,
            _bounds.Left,
            _bounds.Top,
            _bounds.Right - _bounds.Left,
            _bounds.Bottom - _bounds.Top,
            NoActivate);
    }

    private static BitmapSource? Capture(WindowRect bounds, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = IntPtr.Zero;
        var bitmapHandle = IntPtr.Zero;
        var previousBitmap = IntPtr.Zero;
        try
        {
            if (screenDc == IntPtr.Zero ||
                (memoryDc = CreateCompatibleDC(screenDc)) == IntPtr.Zero ||
                (bitmapHandle = CreateCompatibleBitmap(screenDc, width, height)) == IntPtr.Zero)
            {
                return null;
            }

            previousBitmap = SelectObject(memoryDc, bitmapHandle);
            if (!BitBlt(
                    memoryDc, 0, 0, width, height,
                    screenDc, bounds.Left, bounds.Top,
                    SourceCopyWithLayeredWindows))
            {
                return null;
            }

            var bitmap = Imaging.CreateBitmapSourceFromHBitmap(
                bitmapHandle,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            return bitmap;
        }
        finally
        {
            if (previousBitmap != IntPtr.Zero && memoryDc != IntPtr.Zero)
            {
                SelectObject(memoryDc, previousBitmap);
            }
            if (bitmapHandle != IntPtr.Zero)
            {
                DeleteObject(bitmapHandle);
            }
            if (memoryDc != IntPtr.Zero)
            {
                DeleteDC(memoryDc);
            }
            if (screenDc != IntPtr.Zero)
            {
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out WindowRect bounds);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr windowHandle, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr deviceContext, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr graphicsObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(
        IntPtr destination,
        int destinationX,
        int destinationY,
        int width,
        int height,
        IntPtr source,
        int sourceX,
        int sourceY,
        uint rasterOperation);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr graphicsObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
