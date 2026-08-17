using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace unity_cli_ui.Interop;

internal static class WindowMaximizeBounds
{
    private const int GetMinMaxInfoMessage = 0x0024;
    private const uint MonitorDefaultToNearest = 2;
    private const uint GetAutoHideBarEx = 0x000B;
    private const int AutoHideRevealStrip = 2;

    public static void Attach(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var source = HwndSource.FromHwnd(handle)
            ?? throw new InvalidOperationException("The window source is not initialized.");
        source.AddHook(WindowProcedure);
        window.Closed += (_, _) => source.RemoveHook(WindowProcedure);
    }

    internal static NativeRect ReserveAutoHideStrip(NativeRect bounds, AppBarEdge edge)
    {
        switch (edge)
        {
            case AppBarEdge.Left:
                bounds.Left += AutoHideRevealStrip;
                break;
            case AppBarEdge.Top:
                bounds.Top += AutoHideRevealStrip;
                break;
            case AppBarEdge.Right:
                bounds.Right -= AutoHideRevealStrip;
                break;
            case AppBarEdge.Bottom:
                bounds.Bottom -= AutoHideRevealStrip;
                break;
        }

        return bounds;
    }

    private static IntPtr WindowProcedure(
        IntPtr windowHandle,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter,
        ref bool handled)
    {
        if (message != GetMinMaxInfoMessage)
        {
            return IntPtr.Zero;
        }

        var monitorHandle = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            return IntPtr.Zero;
        }

        var targetBounds = monitorInfo.WorkArea;
        if (SameBounds(targetBounds, monitorInfo.Monitor) &&
            TryGetAutoHideEdge(monitorInfo.Monitor, out var edge))
        {
            targetBounds = ReserveAutoHideStrip(targetBounds, edge);
        }

        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(longParameter);
        minMaxInfo.MaxPosition.X = targetBounds.Left - monitorInfo.Monitor.Left;
        minMaxInfo.MaxPosition.Y = targetBounds.Top - monitorInfo.Monitor.Top;
        minMaxInfo.MaxSize.X = targetBounds.Right - targetBounds.Left;
        minMaxInfo.MaxSize.Y = targetBounds.Bottom - targetBounds.Top;
        Marshal.StructureToPtr(minMaxInfo, longParameter, false);
        handled = true;
        return IntPtr.Zero;
    }

    private static bool SameBounds(NativeRect first, NativeRect second) =>
        first.Left == second.Left && first.Top == second.Top &&
        first.Right == second.Right && first.Bottom == second.Bottom;

    private static bool TryGetAutoHideEdge(NativeRect monitorBounds, out AppBarEdge edge)
    {
        for (edge = AppBarEdge.Left; edge <= AppBarEdge.Bottom; edge++)
        {
            var appBar = new AppBarData
            {
                Size = Marshal.SizeOf<AppBarData>(),
                Edge = edge,
                Bounds = monitorBounds
            };
            if (SHAppBarMessage(GetAutoHideBarEx, ref appBar) != IntPtr.Zero)
            {
                return true;
            }
        }

        return false;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfo monitorInfo);

    [DllImport("shell32.dll")]
    private static extern IntPtr SHAppBarMessage(uint message, ref AppBarData data);

    internal enum AppBarEdge : uint
    {
        Left,
        Top,
        Right,
        Bottom
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int Size;
        public IntPtr WindowHandle;
        public uint CallbackMessage;
        public AppBarEdge Edge;
        public NativeRect Bounds;
        public IntPtr Parameter;
    }
}
