using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using RadialCommandWheel.Core;
using RadialCommandWheel.Services;
using RadialCommandWheel.UI;
using WpfPoint = System.Windows.Point;

namespace RadialCommandWheel.UI;

public sealed class OverlayWindow : Window
{
    private readonly MonitorBounds _monitor;
    private readonly Grid _root;
    private readonly RadialWheelControl _wheel;
    private ThemeDefinition _theme;
    private HwndSource? _source;

    public OverlayWindow(MonitorBounds monitor, ThemeDefinition theme, IReadOnlyList<WheelItem> items)
    {
        _monitor = monitor;
        _theme = theme;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _wheel = new RadialWheelControl
        {
            IsOverlay = true,
            Items = items,
            Theme = theme,
            DesiredDiameter = theme.WheelSize,
            WheelTitle = LocalizationService.Get("MainCenter"),
            CanGoBack = false
        };
        _wheel.ItemActivated += item => ItemInvoked?.Invoke(item);
        _wheel.GroupActivated += item => GroupInvoked?.Invoke(item);
        _wheel.BackActivated += () => BackRequested?.Invoke();
        _wheel.CancelRequested += () => CancelRequested?.Invoke();

        _root = new Grid { Background = CreateBackdropBrush(theme) };
        _root.Children.Add(_wheel);
        _root.MouseLeftButtonDown += (_, eventArgs) =>
        {
            if (eventArgs.Handled)
            {
                return;
            }

            eventArgs.Handled = true;
            CancelRequested?.Invoke();
        };
        Content = _root;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        SizeChanged += (_, _) => ComputeCenter();
        PreviewMouseRightButtonDown += (_, eventArgs) =>
        {
            eventArgs.Handled = true;
            CancelRequested?.Invoke();
        };
    }

    public event Action<WheelItem>? ItemInvoked;
    public event Action<WheelItem>? GroupInvoked;
    public event Action? BackRequested;
    public event Action? CancelRequested;

    public void UpdateTheme(ThemeDefinition theme)
    {
        _theme = theme;
        _wheel.Theme = theme;
        _wheel.DesiredDiameter = theme.WheelSize;
        _root.Background = CreateBackdropBrush(theme);
        ComputeCenter();
    }

    public void SetWheel(string title, IReadOnlyList<WheelItem> items, bool canGoBack)
    {
        _wheel.SetWheel(title, items, canGoBack);
        ComputeCenter();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLong(handle, NativeMethods.GwlExstyle);
        NativeMethods.SetWindowLong(handle, NativeMethods.GwlExstyle,
            style | NativeMethods.WsExToolwindow | NativeMethods.WsExNoactivate);
        NativeMethods.SetWindowPos(handle, NativeMethods.HwndTopmost,
            _monitor.WorkArea.Left, _monitor.WorkArea.Top,
            _monitor.WorkArea.Width, _monitor.WorkArea.Height,
            NativeMethods.SwpNoactivate | NativeMethods.SwpShowwindow);
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WindowProc);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ComputeCenter();
        if (!_theme.AnimationEnabled)
        {
            return;
        }

        var duration = TimeSpan.FromMilliseconds(_theme.AnimationDurationMs);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, duration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
        _wheel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 1, duration));
    }

    private void ComputeCenter()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var placement = OverlayPlacement.Calculate(
            _monitor.WorkArea.Width,
            _monitor.WorkArea.Height,
            _monitor.Cursor.X - _monitor.WorkArea.Left,
            _monitor.Cursor.Y - _monitor.WorkArea.Top,
            ActualWidth,
            ActualHeight,
            _theme.WheelSize);
        _wheel.SetCenter(placement.Center);
        _wheel.DesiredDiameter = placement.Diameter;
        _wheel.RenderTransformOrigin = new WpfPoint(
            placement.Center.X / ActualWidth, placement.Center.Y / ActualHeight);
        _wheel.RenderTransform = new ScaleTransform(1, 1);
        if (_theme.AnimationEnabled)
        {
            var scale = new ScaleTransform(0.92, 0.92);
            _wheel.RenderTransform = scale;
            var duration = TimeSpan.FromMilliseconds(_theme.AnimationDurationMs);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.92, 1, duration) { EasingFunction = new QuadraticEase() });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.92, 1, duration) { EasingFunction = new QuadraticEase() });
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmMouseActivate)
        {
            handled = true;
            return new IntPtr(3); // MA_NOACTIVATE
        }

        return IntPtr.Zero;
    }

    private static SolidColorBrush CreateBackdropBrush(ThemeDefinition theme)
    {
        var color = Colors.Black;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(theme.Background);
        }
        catch
        {
            // The model validator falls back to a valid tint on save.
        }

        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Clamp(theme.Dimming * 255, 1, 255), color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_source is not null)
        {
            _source.RemoveHook(WindowProc);
            _source = null;
        }

        base.OnClosed(e);
    }
}

