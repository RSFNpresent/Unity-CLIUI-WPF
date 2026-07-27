using unity_cli_ui.Services;

namespace UnityCliUi.DirectTests;

internal static class SystemAnimationPolicyTests
{
    public static Task RunAsync()
    {
        Require(SystemAnimationPolicy.CurrentPreference == AnimationPreference.FollowWindows);
        Require(!SystemAnimationPolicy.AlwaysEnabled);

        Require(SystemAnimationPolicy.Evaluate(
            AnimationPreference.AlwaysEnabled,
            new ThrowingWindowsAnimationSettings()));

        Require(SystemAnimationPolicy.Evaluate(
            AnimationPreference.FollowWindows,
            new StubWindowsAnimationSettings(true, true)));
        Require(!SystemAnimationPolicy.Evaluate(
            AnimationPreference.FollowWindows,
            new StubWindowsAnimationSettings(false, true)));
        Require(!SystemAnimationPolicy.Evaluate(
            AnimationPreference.FollowWindows,
            new StubWindowsAnimationSettings(true, false)));
        Require(!SystemAnimationPolicy.Evaluate(
            AnimationPreference.FollowWindows,
            new StubWindowsAnimationSettings(false, false)));

        return Task.CompletedTask;
    }

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("System animation policy assertion failed.");
        }
    }

    private sealed record StubWindowsAnimationSettings(
        bool ClientAreaAnimation,
        bool MenuAnimation) : IWindowsAnimationSettings;

    private sealed class ThrowingWindowsAnimationSettings : IWindowsAnimationSettings
    {
        public bool ClientAreaAnimation => throw new InvalidOperationException("Settings should not be read.");

        public bool MenuAnimation => throw new InvalidOperationException("Settings should not be read.");
    }
}
