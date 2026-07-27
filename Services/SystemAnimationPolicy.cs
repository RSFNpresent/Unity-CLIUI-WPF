using System.Windows;

namespace unity_cli_ui.Services;

public enum AnimationPreference
{
    AlwaysEnabled,
    FollowWindows
}

public interface IWindowsAnimationSettings
{
    bool ClientAreaAnimation { get; }

    bool MenuAnimation { get; }
}

public sealed class WindowsAnimationSettings : IWindowsAnimationSettings
{
    public static WindowsAnimationSettings Instance { get; } = new();

    private WindowsAnimationSettings()
    {
    }

    public bool ClientAreaAnimation => SystemParameters.ClientAreaAnimation;

    public bool MenuAnimation => SystemParameters.MenuAnimation;
}

public static class SystemAnimationPolicy
{
    // Keep the product-wide choice in one place so every animation follows Windows consistently.
    public const AnimationPreference CurrentPreference = AnimationPreference.FollowWindows;

    public static bool AlwaysEnabled => CurrentPreference == AnimationPreference.AlwaysEnabled;

    public static bool AnimationsEnabled => Evaluate(CurrentPreference, WindowsAnimationSettings.Instance);

    public static bool Evaluate(
        AnimationPreference preference,
        IWindowsAnimationSettings windowsSettings)
    {
        ArgumentNullException.ThrowIfNull(windowsSettings);

        return preference switch
        {
            AnimationPreference.AlwaysEnabled => true,
            AnimationPreference.FollowWindows =>
                windowsSettings.ClientAreaAnimation && windowsSettings.MenuAnimation,
            _ => throw new ArgumentOutOfRangeException(nameof(preference), preference, null)
        };
    }
}
