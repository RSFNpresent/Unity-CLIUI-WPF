using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace unity_cli_ui.Services;

public static class SmoothScrollBehavior
{
    private static readonly ConditionalWeakTable<ScrollViewer, ScrollAnimation> Animations = new();

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(SmoothScrollBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not UIElement element)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            element.PreviewMouseWheel += Element_PreviewMouseWheel;
        }
        else
        {
            element.PreviewMouseWheel -= Element_PreviewMouseWheel;
        }
    }

    private static void Element_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!SystemAnimationPolicy.AnimationsEnabled || e.Delta == 0)
        {
            return;
        }

        var viewer = FindScrollViewer(e.OriginalSource as DependencyObject, e.Delta)
            ?? (sender as ScrollViewer is { } direct && CanScroll(direct, e.Delta) ? direct : null);
        if (viewer is null)
        {
            return;
        }

        e.Handled = true;
        var distance = e.Delta / 120d * 104d;
        Animations.GetValue(viewer, static item => new ScrollAnimation(item)).Retarget(-distance);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject? origin, int delta)
    {
        for (var current = origin; current is not null; current = GetParent(current))
        {
            if (current is ScrollViewer viewer && CanScroll(viewer, delta))
            {
                return viewer;
            }
        }
        return null;
    }

    private static bool CanScroll(ScrollViewer viewer, int delta) =>
        viewer.ScrollableHeight > 0 &&
        ((delta > 0 && viewer.VerticalOffset > 0) ||
         (delta < 0 && viewer.VerticalOffset < viewer.ScrollableHeight));

    private static DependencyObject? GetParent(DependencyObject child) =>
        child is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(child)
            : LogicalTreeHelper.GetParent(child);

    private sealed class ScrollAnimation
    {
        private readonly ScrollViewer _viewer;
        private double _start;
        private double _target;
        private long _startedAt;
        private bool _running;

        public ScrollAnimation(ScrollViewer viewer)
        {
            _viewer = viewer;
            _target = viewer.VerticalOffset;
        }

        public void Retarget(double delta)
        {
            var origin = _running ? _target : _viewer.VerticalOffset;
            _start = _viewer.VerticalOffset;
            _target = Math.Clamp(origin + delta, 0, _viewer.ScrollableHeight);
            _startedAt = Stopwatch.GetTimestamp();

            if (_running)
            {
                return;
            }

            _running = true;
            CompositionTarget.Rendering += CompositionTarget_Rendering;
        }

        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            var progress = Math.Clamp(
                Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds / 300d,
                0,
                1);
            var eased = 1 - Math.Pow(1 - progress, 5);
            _viewer.ScrollToVerticalOffset(_start + ((_target - _start) * eased));

            if (progress < 1)
            {
                return;
            }

            _viewer.ScrollToVerticalOffset(_target);
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            _running = false;
        }
    }
}
