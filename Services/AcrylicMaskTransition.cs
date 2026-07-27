using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace unity_cli_ui.Services;

internal static class AcrylicMaskTransition
{
    public static async Task RunAsync(
        Dispatcher dispatcher,
        FrameworkElement? mask,
        double maskFrom,
        double maskTo,
        TimeSpan duration,
        IEasingFunction easing,
        Func<Task> contentAnimation,
        CancellationToken cancellationToken,
        Action? applyWindowState = null,
        double horizontalOffset = 22)
    {
        var originalOpacity = mask?.Opacity ?? 0;
        var originalVisibility = mask?.Visibility ?? Visibility.Collapsed;
        var originalHitTest = mask?.IsHitTestVisible ?? false;
        var originalTransform = mask?.RenderTransform;
        var originalTransformOrigin = mask?.RenderTransformOrigin ?? default;
        try
        {
            TranslateTransform? translation = null;
            if (mask is not null)
            {
                var isReveal = maskTo < maskFrom;
                translation = new TranslateTransform(isReveal ? horizontalOffset : 0, 0);
                mask.RenderTransform = translation;
                mask.RenderTransformOrigin = new Point(0.5, 0.5);
                mask.IsHitTestVisible = false;
                mask.Visibility = Visibility.Visible;
                mask.Opacity = maskFrom;
            }

            applyWindowState?.Invoke();
            if (applyWindowState is not null)
            {
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
            }

            var contentTask = contentAnimation();
            if (mask is null)
            {
                await contentTask;
                return;
            }

            var maskAnimations = new[]
            {
                ((DependencyObject)mask, UIElement.OpacityProperty, maskTo),
                ((DependencyObject)translation!, TranslateTransform.XProperty,
                    maskTo < maskFrom ? 0 : -horizontalOffset)
            };
            await Task.WhenAll(
                contentTask,
                WpfAnimationRunner.RunAsync(
                    maskAnimations, duration, easing, cancellationToken));
        }
        finally
        {
            if (mask is not null)
            {
                mask.BeginAnimation(UIElement.OpacityProperty, null);
                mask.Opacity = originalOpacity;
                mask.Visibility = originalVisibility;
                mask.IsHitTestVisible = originalHitTest;
                mask.RenderTransform = originalTransform;
                mask.RenderTransformOrigin = originalTransformOrigin;
            }
        }
    }
}
