using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace unity_cli_ui.Services;

public static class UiMotionService
{
    private const double TileStartOpacity = 0;
    private const double TileStartScale = 0.78;
    private const double TileStartOffsetX = 26;

    private static readonly IEasingFunction TileEase = new ExponentialEase
    {
        Exponent = 6,
        EasingMode = EasingMode.EaseOut
    };

    public static void PreparePageEntrance(FrameworkElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var translation = EnsureTranslation(target);
        target.Opacity = 0;
        translation.X = 30;
    }

    public static void PlayPageEntrance(FrameworkElement target)
    {
        if (!SystemAnimationPolicy.AnimationsEnabled)
        {
            ApplyPageFinalPose(target);
            return;
        }

        PreparePageEntrance(target);
        QueueStoryboard("Motion.PageEnterStoryboard", target);
    }

    public static void PlayPreparedPageEntrance(FrameworkElement target)
    {
        if (!SystemAnimationPolicy.AnimationsEnabled)
        {
            ApplyPageFinalPose(target);
            return;
        }

        QueueStoryboard("Motion.PageEnterStoryboard", target);
    }

    public static void PrepareStartMenuEntrance(IEnumerable<FrameworkElement> targets)
    {
        foreach (var target in targets)
        {
            PrepareTileEntrance(target);
        }
    }

    public static void PlayStartMenuEntrance(IEnumerable<FrameworkElement> targets)
    {
        if (!SystemAnimationPolicy.AnimationsEnabled)
        {
            foreach (var target in targets)
            {
                ApplyTileFinalPose(target);
            }
            return;
        }

        var index = 0;
        foreach (var target in targets)
        {
            PrepareTileEntrance(target);
            QueueTileEntrance(target, TimeSpan.FromMilliseconds(120 + (index * 64)));
            index++;
        }
    }

    private static void QueueStoryboard(string resourceKey, FrameworkElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!SystemAnimationPolicy.AnimationsEnabled)
        {
            ApplyPageFinalPose(target);
            return;
        }

        target.Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            if (!SystemAnimationPolicy.AnimationsEnabled)
            {
                ApplyPageFinalPose(target);
                return;
            }

            if (!target.IsVisible ||
                Application.Current?.TryFindResource(resourceKey) is not Storyboard storyboard)
            {
                return;
            }

            var translation = EnsureTranslation(target);
            target.Opacity = 1;
            translation.X = 0;
            storyboard.Begin(target, HandoffBehavior.SnapshotAndReplace, isControllable: false);
        });
    }

    private static TranslateTransform EnsureTranslation(FrameworkElement target)
    {
        if (target.RenderTransform is TranslateTransform translation)
        {
            return translation;
        }

        translation = new TranslateTransform();
        target.RenderTransform = translation;
        target.RenderTransformOrigin = new Point(0.5, 0.5);
        return translation;
    }

    private static void PrepareTileEntrance(FrameworkElement target)
    {
        var scale = new ScaleTransform(TileStartScale, TileStartScale);
        var translation = new TranslateTransform(TileStartOffsetX, 0);
        var transforms = new TransformGroup();
        transforms.Children.Add(scale);
        transforms.Children.Add(translation);
        target.RenderTransform = transforms;
        target.RenderTransformOrigin = new Point(0.5, 0.5);
        target.Opacity = TileStartOpacity;
    }

    private static void QueueTileEntrance(FrameworkElement target, TimeSpan delay)
    {
        target.Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            if (!SystemAnimationPolicy.AnimationsEnabled)
            {
                ApplyTileFinalPose(target);
                return;
            }

            if (!target.IsVisible ||
                target.RenderTransform is not TransformGroup transforms ||
                transforms.Children.ElementAtOrDefault(0) is not ScaleTransform scale ||
                transforms.Children.ElementAtOrDefault(1) is not TranslateTransform translation)
            {
                return;
            }

            var duration = TimeSpan.FromMilliseconds(480);
            var opacity = CreateAnimation(TileStartOpacity, 1, duration, delay);
            var scaleX = CreateAnimation(TileStartScale, 1, duration, delay);
            var scaleY = CreateAnimation(TileStartScale, 1, duration, delay);
            var translateX = CreateAnimation(TileStartOffsetX, 0, duration, delay);
            opacity.Completed += (_, _) => ApplyTileFinalPose(target);

            target.BeginAnimation(UIElement.OpacityProperty, opacity, HandoffBehavior.SnapshotAndReplace);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX, HandoffBehavior.SnapshotAndReplace);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY, HandoffBehavior.SnapshotAndReplace);
            translation.BeginAnimation(TranslateTransform.XProperty, translateX, HandoffBehavior.SnapshotAndReplace);
        });
    }

    private static void ApplyPageFinalPose(FrameworkElement target)
    {
        var translation = EnsureTranslation(target);
        target.BeginAnimation(UIElement.OpacityProperty, null);
        translation.BeginAnimation(TranslateTransform.XProperty, null);
        target.Opacity = 1;
        translation.X = 0;
    }

    private static void ApplyTileFinalPose(FrameworkElement target)
    {
        target.BeginAnimation(UIElement.OpacityProperty, null);
        target.Opacity = 1;
        if (target.RenderTransform is not TransformGroup transforms ||
            transforms.Children.ElementAtOrDefault(0) is not ScaleTransform scale ||
            transforms.Children.ElementAtOrDefault(1) is not TranslateTransform translation)
        {
            return;
        }

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        translation.BeginAnimation(TranslateTransform.XProperty, null);
        scale.ScaleX = 1;
        scale.ScaleY = 1;
        translation.X = 0;
    }

    private static DoubleAnimation CreateAnimation(
        double from,
        double to,
        TimeSpan duration,
        TimeSpan delay) => new(from, to, duration)
    {
        BeginTime = delay,
        EasingFunction = TileEase,
        FillBehavior = FillBehavior.HoldEnd
    };
}
