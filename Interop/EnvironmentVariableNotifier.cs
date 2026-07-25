using System.Runtime.InteropServices;

namespace unity_cli_ui.Interop;

internal static class EnvironmentVariableNotifier
{
    private static readonly IntPtr BroadcastHandle = new(0xffff);
    private const uint SettingChangeMessage = 0x001A;
    private const uint AbortIfHung = 0x0002;

    public static void NotifyEnvironmentChanged()
    {
        _ = SendMessageTimeout(
            BroadcastHandle,
            SettingChangeMessage,
            UIntPtr.Zero,
            "Environment",
            AbortIfHung,
            1000,
            out _);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        string lParam,
        uint flags,
        uint timeout,
        out UIntPtr result);
}
