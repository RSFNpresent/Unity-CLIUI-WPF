using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace unity_cli_ui.Interop;

internal static class NativeWindowMotion
{
    private const uint NoSize = 0x0001;
    private const uint NoZOrder = 0x0004;
    private const uint NoActivate = 0x0010;
    private const uint NoOwnerZOrder = 0x0200;

    public static bool TryGetTop(Window window, out int top)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero && GetWindowRect(handle, out var bounds))
        {
            top = bounds.Top;
            return true;
        }

        top = 0;
        return false;
    }

    public static bool SetTop(Window window, int top)
    {
        var handle = new WindowInteropHelper(window).Handle;
        return handle != IntPtr.Zero &&
               GetWindowRect(handle, out var bounds) &&
               SetWindowPos(
                   handle, IntPtr.Zero, bounds.Left, top, 0, 0,
                   NoSize | NoZOrder | NoActivate | NoOwnerZOrder);
    }

    public static Task AnimateToTopAsync(
        Window window,
        int targetTop,
        TimeSpan duration,
        IEasingFunction easing,
        CancellationToken cancellationToken)
    {
        if (!TryGetTop(window, out var startTop) || startTop == targetTop)
        {
            return Task.CompletedTask;
        }

        if (duration <= TimeSpan.Zero)
        {
            SetTop(window, targetTop);
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedAt = Stopwatch.GetTimestamp();
        var timer = new DispatcherTimer(DispatcherPriority.Render, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        timer.Tick += Tick;
        timer.Start();
        return completion.Task;

        void Tick(object? sender, EventArgs args)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                timer.Stop();
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            var progress = Math.Clamp(
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds / duration.TotalMilliseconds,
                0,
                1);
            var eased = easing.Ease(progress);
            var top = (int)Math.Round(startTop + ((targetTop - startTop) * eased));
            SetTop(window, top);
            if (progress < 1)
            {
                return;
            }

            timer.Stop();
            SetTop(window, targetTop);
            completion.TrySetResult();
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out WindowRect bounds);

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
