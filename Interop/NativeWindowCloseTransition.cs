using System.Windows;
using System.Windows.Media.Animation;

namespace unity_cli_ui.Interop;

internal static class NativeWindowCloseTransition
{
    public static async Task RunAsync(
        Window window,
        TimeSpan duration,
        IEasingFunction easing,
        CancellationToken cancellationToken,
        Action permitClose)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(easing);
        ArgumentNullException.ThrowIfNull(permitClose);
        cancellationToken.ThrowIfCancellationRequested();

        using var snapshot = duration > TimeSpan.Zero && window.IsVisible
            ? WindowCloseSnapshot.TryCreate(window)
            : null;
        if (snapshot is null)
        {
            CloseWithoutAnimation(window, permitClose);
            return;
        }

        try
        {
            snapshot.Show();
            window.Hide();
            await snapshot.FadeOutAsync(duration, easing, cancellationToken);
            permitClose();
            window.Close();
        }
        finally
        {
            if (window.IsLoaded)
            {
                window.Show();
            }
        }
    }

    private static void CloseWithoutAnimation(Window window, Action permitClose)
    {
        window.Hide();
        try
        {
            permitClose();
            window.Close();
        }
        finally
        {
            if (window.IsLoaded)
            {
                window.Show();
            }
        }
    }
}
