using System.Windows;
using System.Windows.Media.Animation;

namespace unity_cli_ui.Services;

internal static class WpfAnimationRunner
{
    public static async Task RunAsync(
        IReadOnlyList<(DependencyObject Target, DependencyProperty Property, double Value)> animations,
        TimeSpan duration,
        IEasingFunction easing,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clocks = new List<(DependencyObject Target, DependencyProperty Property)>(animations.Count);

        for (var index = 0; index < animations.Count; index++)
        {
            var item = animations[index];
            var animation = new DoubleAnimation(item.Value, duration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd
            };
            if (index == 0)
            {
                animation.Completed += (_, _) => completion.TrySetResult();
            }

            BeginAnimation(item.Target, item.Property, animation);
            clocks.Add((item.Target, item.Property));
        }

        using var registration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));
        try
        {
            await completion.Task;
        }
        finally
        {
            foreach (var clock in clocks)
            {
                BeginAnimation(clock.Target, clock.Property, null);
            }
        }
    }

    private static void BeginAnimation(
        DependencyObject target,
        DependencyProperty property,
        AnimationTimeline? animation)
    {
        switch (target)
        {
            case UIElement element:
                element.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
                break;
            case Animatable animatable:
                animatable.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
                break;
            default:
                throw new ArgumentException(
                    "The animation target does not support WPF animations.",
                    nameof(target));
        }
    }
}
