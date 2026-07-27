using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace unity_cli_ui.Interop;

internal static class AcrylicWindow
{
    public static bool Enable(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            return false;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        return SetEnabled(window, enabled: true);
    }

    public static bool SetEnabled(Window window, bool enabled) => Apply(window, enabled);

    private static bool Apply(Window window, bool isActive)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        if (HwndSource.FromHwnd(handle) is HwndSource source)
        {
            source.CompositionTarget.BackgroundColor = isActive
                ? Colors.Transparent
                : Color.FromRgb(242, 242, 242);
        }

        var policy = new AccentPolicy
        {
            AccentState = isActive ? AccentState.EnableAcrylicBlurBehind : AccentState.EnableGradient,
            AccentFlags = isActive ? 2 : 0,
            GradientColor = ToAbgr(isActive ? (byte)0xB8 : (byte)0xFF, 0xF2, 0xF2, 0xF2)
        };

        var policySize = Marshal.SizeOf<AccentPolicy>();
        var policyPointer = Marshal.AllocHGlobal(policySize);
        try
        {
            Marshal.StructureToPtr(policy, policyPointer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttribute.AccentPolicy,
                Data = policyPointer,
                SizeOfData = policySize
            };
            return SetWindowCompositionAttribute(handle, ref data) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(policyPointer);
        }
    }

    private static int ToAbgr(byte alpha, byte red, byte green, byte blue) =>
        alpha << 24 | blue << 16 | green << 8 | red;

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(
        IntPtr windowHandle,
        ref WindowCompositionAttributeData data);

    private enum AccentState
    {
        EnableGradient = 1,
        EnableAcrylicBlurBehind = 4
    }

    private enum WindowCompositionAttribute
    {
        AccentPolicy = 19
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }
}
