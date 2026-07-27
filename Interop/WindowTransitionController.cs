using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using unity_cli_ui.Services;

namespace unity_cli_ui.Interop;

/// <summary>
/// Coordinates short WPF content transitions around native window state changes.
/// Native acrylic is switched by the caller at the midpoint of a WPF cross-fade.
/// </summary>
internal sealed class WindowTransitionController : IDisposable
{
    private const int MinimizeTravel = 72;
    private static readonly IEasingFunction EnterEasing = new ExponentialEase
        { Exponent = 6, EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction WindowStateEasing = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction ExitEasing = new CubicEase { EasingMode = EasingMode.EaseIn };
    private readonly Window _window;
    private readonly FrameworkElement _content;
    private readonly FrameworkElement? _acrylicTransitionMask;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _translation = new(0, 0);
    private readonly Transform _originalTransform;
    private readonly Point _originalTransformOrigin;
    private readonly double _originalOpacity;
    private CancellationTokenSource? _acrylicRequestCancellation;
    private bool? _requestedAcrylicState;
    private bool? _appliedAcrylicState;
    private int? _windowTopBeforeMinimize;
    private int _windowStateAnimationRequests;
    private WindowState _lastWindowState;
    private bool _ignoreStateChanged;
    private bool _allowClose;
    private bool _closeInProgress;
    private bool _entrancePlayed;
    private bool _disposed;
    public TimeSpan EntranceDuration { get; set; } = TimeSpan.FromMilliseconds(360);
    public TimeSpan ExitDuration { get; set; } = TimeSpan.FromMilliseconds(280);
    public TimeSpan AcrylicCoverDuration { get; set; } = TimeSpan.FromMilliseconds(200);
    public TimeSpan AcrylicRevealDuration { get; set; } = TimeSpan.FromMilliseconds(300);

    public WindowTransitionController(
        Window window,
        FrameworkElement content,
        FrameworkElement? acrylicTransitionMask = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(content);

        _window = window;
        _content = content;
        _acrylicTransitionMask = acrylicTransitionMask;
        _originalTransform = content.RenderTransform;
        _originalTransformOrigin = content.RenderTransformOrigin;
        _originalOpacity = content.Opacity;
        _lastWindowState = window.WindowState;

        var transforms = new TransformGroup();
        if (_originalTransform != Transform.Identity)
        {
            transforms.Children.Add(_originalTransform);
        }

        transforms.Children.Add(_scale);
        transforms.Children.Add(_translation);
        content.RenderTransform = transforms;
        content.RenderTransformOrigin = new Point(0.5, 0.5);

        window.ContentRendered += Window_ContentRendered;
        window.StateChanged += Window_StateChanged;
        window.Closing += Window_Closing;
        window.Closed += Window_Closed;

        if (!window.IsLoaded)
        {
            ApplyContentPose(0, 0.985, 18);
        }
        else
        {
            RequestEntrance();
        }
    }

    public Task PlayEntranceAsync(CancellationToken cancellationToken = default) =>
        RunOnWindowThreadAsync(() => PlayEntranceCoreAsync(cancellationToken));

    public Task MinimizeAsync(CancellationToken cancellationToken = default) =>
        RunOnWindowThreadAsync(() => MinimizeCoreAsync(cancellationToken));

    public Task ToggleMaximizeAsync(CancellationToken cancellationToken = default) =>
        RunOnWindowThreadAsync(() => ToggleMaximizeCoreAsync(cancellationToken));

    public Task CloseAsync(CancellationToken cancellationToken = default) =>
        RunOnWindowThreadAsync(() => CloseCoreAsync(cancellationToken));

    /// <summary>
    /// Cross-fades a WPF mask while the caller changes the native acrylic state.
    /// When no mask is supplied, the window content performs a subtle opacity cross-fade.
    /// </summary>
    public Task SetAcrylicEnabledAsync(
        bool enabled,
        Action<bool> applyNativeState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applyNativeState);
        return RunOnWindowThreadAsync(
            () => SetAcrylicEnabledCoreAsync(enabled, applyNativeState, cancellationToken));
    }

    public void SetKnownAcrylicState(bool enabled)
    {
        _requestedAcrylicState = enabled;
        _appliedAcrylicState = enabled;
    }

    // These request methods are intended for synchronous WPF event handlers.
    public void RequestEntrance() => Observe(PlayEntranceAsync());
    public void RequestMinimize() => Observe(MinimizeAsync());
    public void RequestToggleMaximize() => Observe(ToggleMaximizeAsync());
    public void RequestClose() => Observe(CloseAsync());
    public void RequestSetAcrylicEnabled(bool enabled, Action<bool> applyNativeState)
    {
        if (_windowStateAnimationRequests > 0)
        {
            _acrylicRequestCancellation?.Cancel();
            _requestedAcrylicState = enabled;
            if (_appliedAcrylicState != enabled)
            {
                applyNativeState(enabled);
                _appliedAcrylicState = enabled;
            }
            return;
        }

        if (_disposed ||
            (_acrylicRequestCancellation is { IsCancellationRequested: false } &&
             _requestedAcrylicState == enabled) ||
            (_acrylicRequestCancellation is null && _appliedAcrylicState == enabled))
        {
            return;
        }

        _requestedAcrylicState = enabled;
        _acrylicRequestCancellation?.Cancel();
        var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token);
        _acrylicRequestCancellation = requestCancellation;
        Observe(RunLatestAcrylicRequestAsync(enabled, applyNativeState, requestCancellation));
    }

    private static bool ShouldAnimate => SystemAnimationPolicy.AnimationsEnabled;

    private async Task RunLatestAcrylicRequestAsync(
        bool enabled,
        Action<bool> applyNativeState,
        CancellationTokenSource requestCancellation)
    {
        try
        {
            await SetAcrylicEnabledAsync(enabled, applyNativeState, requestCancellation.Token);
        }
        finally
        {
            if (ReferenceEquals(_acrylicRequestCancellation, requestCancellation))
            {
                _acrylicRequestCancellation = null;
            }

            requestCancellation.Dispose();
        }
    }

    private async Task PlayEntranceCoreAsync(CancellationToken cancellationToken)
    {
        if (_entrancePlayed || _disposed)
        {
            return;
        }

        _entrancePlayed = true;
        await WithWindowStateGateAsync(async token =>
        {
            if (!ShouldAnimate)
            {
                ApplyContentPose(_originalOpacity, 1, 0);
                return;
            }

            await AnimateWindowStateAndMaskAsync(
                _originalOpacity, 1, 0, 0.60, 0, EntranceDuration, EnterEasing, token);
        }, cancellationToken);
    }

    private async Task MinimizeCoreAsync(CancellationToken cancellationToken)
    {
        if (_disposed || _window.WindowState == WindowState.Minimized)
        {
            return;
        }

        await WithWindowStateGateAsync(async token =>
        {
            if (ShouldAnimate)
            {
                var contentAnimation = AnimateWindowStateAndMaskAsync(
                    0, 1, 0, 0.68, 0, ExitDuration, ExitEasing, token,
                    maskOffsetX: 0);
                if (NativeWindowMotion.TryGetTop(_window, out var originalTop))
                {
                    _windowTopBeforeMinimize = originalTop;
                    try
                    {
                        await Task.WhenAll(
                            contentAnimation,
                            NativeWindowMotion.AnimateToTopAsync(
                                _window, originalTop + MinimizeTravel,
                                ExitDuration, ExitEasing, token));
                    }
                    catch
                    {
                        NativeWindowMotion.SetTop(_window, originalTop);
                        _windowTopBeforeMinimize = null;
                        throw;
                    }
                }
                else
                {
                    await contentAnimation;
                }
            }
            else
            {
                _windowTopBeforeMinimize = null;
            }

            SetWindowState(WindowState.Minimized);
            ApplyContentPose(_originalOpacity, 1, 0);
        }, cancellationToken);
    }

    private async Task ToggleMaximizeCoreAsync(CancellationToken cancellationToken)
    {
        if (_disposed || _window.WindowState == WindowState.Minimized)
        {
            return;
        }

        await WithWindowStateGateAsync(async token =>
        {
            token.ThrowIfCancellationRequested();
            var targetState = _window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            SetWindowState(targetState);
            ApplyContentPose(_originalOpacity, 1, 0);
            await Task.CompletedTask;
        }, cancellationToken);
    }

    private async Task CloseCoreAsync(CancellationToken cancellationToken)
    {
        if (_disposed || _allowClose || _closeInProgress)
        {
            return;
        }
        _closeInProgress = true;
        _acrylicRequestCancellation?.Cancel();
        try
        {
            await WithWindowStateGateAsync(
                token => NativeWindowCloseTransition.RunAsync(
                    _window, ShouldAnimate ? ExitDuration : TimeSpan.Zero,
                    WindowStateEasing, token, () => _allowClose = true),
                cancellationToken);
        }
        finally
        {
            _allowClose = false;
            _closeInProgress = false;
        }
    }
    private async Task SetAcrylicEnabledCoreAsync(
        bool enabled,
        Action<bool> applyNativeState,
        CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        await WithOperationGateAsync(async token =>
        {
            if (!ShouldAnimate)
            {
                applyNativeState(enabled);
                _appliedAcrylicState = enabled;
                return;
            }

            if (_acrylicTransitionMask is null)
            {
                await AnimateContentAsync(_originalOpacity * 0.9, 1, 0,
                    AcrylicCoverDuration, ExitEasing, token);
                applyNativeState(enabled);
                _appliedAcrylicState = enabled;
                await AnimateContentAsync(_originalOpacity, 1, 0,
                    AcrylicRevealDuration, EnterEasing, token);
                return;
            }

            var mask = _acrylicTransitionMask;
            var originalOpacity = mask.Opacity;
            var originalVisibility = mask.Visibility;
            var originalHitTest = mask.IsHitTestVisible;
            try
            {
                mask.IsHitTestVisible = false;
                mask.Visibility = Visibility.Visible;
                mask.Opacity = 0;
                await AnimateOpacityAsync(mask, 1, AcrylicCoverDuration, ExitEasing, token);
                applyNativeState(enabled);
                _appliedAcrylicState = enabled;
                await AnimateOpacityAsync(mask, 0, AcrylicRevealDuration, EnterEasing, token);
            }
            finally
            {
                StopOpacityAnimation(mask);
                mask.Opacity = originalOpacity;
                mask.Visibility = originalVisibility;
                mask.IsHitTestVisible = originalHitTest;
            }
        }, cancellationToken);
    }

    private async Task WithOperationGateAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetimeCancellation.Token);
        var token = linkedCancellation.Token;
        await _operationGate.WaitAsync(token);
        try
        {
            await operation(token);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task WithWindowStateGateAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        _acrylicRequestCancellation?.Cancel();
        _windowStateAnimationRequests++;
        try
        {
            await WithOperationGateAsync(operation, cancellationToken);
        }
        finally
        {
            _windowStateAnimationRequests--;
        }
    }

    private async Task AnimateContentAsync(
        double opacity,
        double scale,
        double translateY,
        TimeSpan duration,
        IEasingFunction easing,
        CancellationToken cancellationToken)
    {
        if (duration <= TimeSpan.Zero)
        {
            ApplyContentPose(opacity, scale, translateY);
            return;
        }

        var animations = new (DependencyObject Target, DependencyProperty Property, double Value)[]
        {
            (_content, UIElement.OpacityProperty, opacity),
            (_scale, ScaleTransform.ScaleXProperty, scale),
            (_scale, ScaleTransform.ScaleYProperty, scale),
            (_translation, TranslateTransform.YProperty, translateY)
        };

        await WpfAnimationRunner.RunAsync(animations, duration, easing, cancellationToken);
        ApplyContentPose(opacity, scale, translateY);
    }

    private Task AnimateWindowStateAndMaskAsync(
        double opacity,
        double scale,
        double translateY,
        double maskFrom,
        double maskTo,
        TimeSpan duration,
        IEasingFunction easing,
        CancellationToken cancellationToken,
        Action? applyWindowState = null,
        double maskOffsetX = 22) => AcrylicMaskTransition.RunAsync(
            _window.Dispatcher,
            _acrylicTransitionMask,
            maskFrom,
            maskTo,
            duration,
            easing,
            () => AnimateContentAsync(
                opacity, scale, translateY, duration, easing, cancellationToken),
            cancellationToken,
            applyWindowState,
            maskOffsetX);

    private static async Task AnimateOpacityAsync(
        UIElement element,
        double opacity,
        TimeSpan duration,
        IEasingFunction easing,
        CancellationToken cancellationToken)
    {
        var animations = new[]
        {
            ((DependencyObject)element, UIElement.OpacityProperty, opacity)
        };
        await WpfAnimationRunner.RunAsync(animations, duration, easing, cancellationToken);
        StopOpacityAnimation(element);
        element.Opacity = opacity;
    }

    private void ApplyContentPose(double opacity, double scale, double translateY)
    {
        _content.BeginAnimation(UIElement.OpacityProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _translation.BeginAnimation(TranslateTransform.YProperty, null);
        _content.Opacity = opacity;
        _scale.ScaleX = scale;
        _scale.ScaleY = scale;
        _translation.Y = translateY;
    }

    private static void StopOpacityAnimation(UIElement element) =>
        element.BeginAnimation(UIElement.OpacityProperty, null);

    private void SetWindowState(WindowState state)
    {
        _ignoreStateChanged = true;
        try
        {
            _window.WindowState = state;
            _lastWindowState = state;
        }
        finally
        {
            _ignoreStateChanged = false;
        }
    }

    private Task RunOnWindowThreadAsync(Func<Task> action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _window.Dispatcher.CheckAccess()
            ? action()
            : _window.Dispatcher.InvokeAsync(action).Task.Unwrap();
    }

    private void Window_ContentRendered(object? sender, EventArgs e) => RequestEntrance();

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        var previousState = _lastWindowState;
        _lastWindowState = _window.WindowState;
        if (_ignoreStateChanged || _disposed)
        {
            return;
        }

        if (previousState == WindowState.Minimized && _window.WindowState != WindowState.Minimized)
        {
            if (_windowTopBeforeMinimize is int originalTop)
            {
                NativeWindowMotion.SetTop(_window, originalTop + MinimizeTravel);
            }
            ApplyContentPose(0, 1, 0);
            Observe(AnimateRestoredContentAsync());
        }
        else if (_window.WindowState != WindowState.Minimized)
        {
            _acrylicRequestCancellation?.Cancel();
            ApplyContentPose(_originalOpacity, 1, 0);
        }
    }

    private async Task AnimateRestoredContentAsync()
    {
        await WithWindowStateGateAsync(async token =>
        {
            var contentAnimation = AnimateWindowStateAndMaskAsync(
                _originalOpacity, 1, 0, 0.74, 0,
                EntranceDuration, WindowStateEasing, token,
                maskOffsetX: 0);
            if (_windowTopBeforeMinimize is not int originalTop)
            {
                await contentAnimation;
                return;
            }

            try
            {
                await Task.WhenAll(
                    contentAnimation,
                    NativeWindowMotion.AnimateToTopAsync(
                        _window, originalTop,
                        EntranceDuration, WindowStateEasing, token));
            }
            finally
            {
                NativeWindowMotion.SetTop(_window, originalTop);
                _windowTopBeforeMinimize = null;
            }
        }, CancellationToken.None);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || _disposed)
        {
            return;
        }

        e.Cancel = true;
        RequestClose();
    }

    private void Window_Closed(object? sender, EventArgs e) => Dispose();

    private static void Observe(Task task)
    {
        _ = ObserveCoreAsync(task);

        static async Task ObserveCoreAsync(Task observedTask)
        {
            try
            {
                await observedTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Window transition failed: {exception}");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _acrylicRequestCancellation?.Cancel();
        _acrylicRequestCancellation = null;
        _window.ContentRendered -= Window_ContentRendered;
        _window.StateChanged -= Window_StateChanged;
        _window.Closing -= Window_Closing;
        _window.Closed -= Window_Closed;
        _lifetimeCancellation.Cancel();

        if (_window.Dispatcher.CheckAccess())
        {
            RestoreContentProperties();
        }

        _lifetimeCancellation.Dispose();
    }

    private void RestoreContentProperties()
    {
        ApplyContentPose(_originalOpacity, 1, 0);
        _content.RenderTransform = _originalTransform;
        _content.RenderTransformOrigin = _originalTransformOrigin;
    }
}
