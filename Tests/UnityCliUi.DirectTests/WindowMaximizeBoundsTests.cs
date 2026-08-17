using unity_cli_ui.Interop;

namespace UnityCliUi.DirectTests;

internal static class WindowMaximizeBoundsTests
{
    public static Task RunAsync()
    {
        var bounds = new WindowMaximizeBounds.NativeRect
        {
            Left = 10,
            Top = 20,
            Right = 110,
            Bottom = 220
        };

        Equal(12, 20, 110, 220, WindowMaximizeBounds.ReserveAutoHideStrip(
            bounds, WindowMaximizeBounds.AppBarEdge.Left));
        Equal(10, 22, 110, 220, WindowMaximizeBounds.ReserveAutoHideStrip(
            bounds, WindowMaximizeBounds.AppBarEdge.Top));
        Equal(10, 20, 108, 220, WindowMaximizeBounds.ReserveAutoHideStrip(
            bounds, WindowMaximizeBounds.AppBarEdge.Right));
        Equal(10, 20, 110, 218, WindowMaximizeBounds.ReserveAutoHideStrip(
            bounds, WindowMaximizeBounds.AppBarEdge.Bottom));

        return Task.CompletedTask;
    }

    private static void Equal(
        int left,
        int top,
        int right,
        int bottom,
        WindowMaximizeBounds.NativeRect actual)
    {
        if (actual.Left != left || actual.Top != top || actual.Right != right || actual.Bottom != bottom)
        {
            throw new InvalidOperationException("Auto-hide taskbar bounds assertion failed.");
        }
    }
}
