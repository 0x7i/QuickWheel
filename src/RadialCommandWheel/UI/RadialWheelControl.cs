using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RadialCommandWheel.Core;
using RadialCommandWheel.Services;
using WpfPoint = System.Windows.Point;

namespace RadialCommandWheel.UI;

public sealed class RadialWheelControl : FrameworkElement
{
    private int _hoveredIndex = -1;
    private int _pressedIndex = -1;
    private bool _backHovered;
    private bool _backPressed;
    private IReadOnlyList<WheelItem> _items = [];
    private ThemeDefinition _theme = ConfigurationDefaults.Create().Themes[0];
    private WpfPoint? _fixedCenter;
    private double _desiredDiameter = 340;

    public RadialWheelControl()
    {
        Focusable = false;
        SnapsToDevicePixels = true;
        Cursor = Cursors.Arrow;
    }

    public event Action<WheelItem>? ItemActivated;
    public event Action? BackActivated;
    public event Action? CancelRequested;
    public event Action<WheelItem>? GroupActivated;

    public bool IsOverlay { get; set; }

    public string WheelTitle { get; set; } = "MAIN";

    public bool CanGoBack { get; set; }

    public IReadOnlyList<WheelItem> Items
    {
        get => _items;
        set
        {
            _items = value ?? [];
            _hoveredIndex = -1;
            _pressedIndex = -1;
            InvalidateVisual();
        }
    }

    public ThemeDefinition Theme
    {
        get => _theme;
        set
        {
            _theme = value ?? throw new ArgumentNullException(nameof(value));
            InvalidateVisual();
        }
    }

    public double DesiredDiameter
    {
        get => _desiredDiameter;
        set
        {
            _desiredDiameter = value;
            InvalidateVisual();
        }
    }

    public void SetCenter(WpfPoint? center)
    {
        _fixedCenter = center;
        InvalidateVisual();
    }

    public void SetWheel(string title, IReadOnlyList<WheelItem> items, bool canGoBack)
    {
        WheelTitle = title;
        Items = items;
        CanGoBack = canGoBack;
        _backHovered = false;
        _backPressed = false;
    }

    public void ResetInteraction()
    {
        _hoveredIndex = -1;
        _pressedIndex = -1;
        _backHovered = false;
        _backPressed = false;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var center = _fixedCenter ?? new WpfPoint(ActualWidth / 2, ActualHeight / 2);
        var maxAvailable = Math.Max(80, Math.Min(center.X, ActualWidth - center.X)
                                           .Min(center.Y, ActualHeight - center.Y) * 2 - 18);
        var diameter = Math.Min(_desiredDiameter, maxAvailable);
        var outerRadius = diameter / 2;
        var metrics = RadialWheelLayout.CalculateMetrics(outerRadius, _items.Count);
        var innerRadius = metrics.InnerRadius;
        var textBrush = CreateBrush(_theme.Text, 1);
        var sectorBrush = CreateBrush(_theme.Sector, _theme.Opacity);
        var accentBrush = CreateBrush(_theme.Accent, 1);

        DrawCenter(drawingContext, center, innerRadius, sectorBrush, accentBrush, textBrush);
        if (_items.Count == 0)
        {
            DrawEmptyState(drawingContext, center, innerRadius, textBrush);
            return;
        }

        var sweep = metrics.SweepDegrees;
        var gap = metrics.GapDegrees;
        for (var index = 0; index < _items.Count; index++)
        {
            var item = _items[index];
            var hovered = index == _hoveredIndex;
            var pressed = index == _pressedIndex && hovered;
            var itemColor = ColorSuggestionService.ResolveItemColor(item, _theme);
            var startAngle = -90 + index * sweep + gap / 2;
            var endAngle = -90 + (index + 1) * sweep - gap / 2;
            var extra = hovered ? 6.0 : 0.0;
            if (pressed)
            {
                extra = 2.0;
            }

            var geometry = metrics.IsSingleItem
                ? CreateFullRingGeometry(center, innerRadius + (hovered ? 1 : 0), outerRadius + extra)
                : CreateSectorGeometry(center, innerRadius + (hovered ? 1 : 0),
                    outerRadius + extra, startAngle, endAngle);
            var baseAlpha = pressed ? 155.0 : hovered ? 128.0 : 74.0;
            var fillAlpha = (byte)Math.Clamp(baseAlpha * _theme.Opacity, 1, 255);
            var fill = new SolidColorBrush(Color.FromArgb(fillAlpha, itemColor.R, itemColor.G, itemColor.B));
            fill.Freeze();
            var edge = new Pen(new SolidColorBrush(Color.FromArgb(
                (byte)Math.Clamp((hovered ? 255.0 : 150.0) * _theme.Opacity, 1, 255),
                itemColor.R, itemColor.G, itemColor.B)),
                hovered ? 2.0 : 1.0);
            edge.Freeze();
            drawingContext.DrawGeometry(fill, edge, geometry);

            if (hovered)
            {
                var glow = new Pen(new SolidColorBrush(Color.FromArgb(45, itemColor.R, itemColor.G, itemColor.B)),
                    7);
                drawingContext.DrawGeometry(null, glow, geometry);
            }

            var angle = -90 + (index + 0.5) * sweep;
            var iconRadius = metrics.IconRadius + (hovered ? 3 : 0);
            var iconCenter = Polar(center, iconRadius, angle);
            DrawIcon(drawingContext, iconCenter, Math.Clamp(outerRadius * 0.12, 17, 30), item, textBrush);

            if (item.ShowName)
            {
                var textRadius = metrics.LabelRadius + (hovered ? 3 : 0);
                var textCenter = Polar(center, textRadius, angle);
                var maxWidth = Math.Clamp(2 * Math.PI * textRadius / _items.Count * 0.82, 48, 122);
                DrawCenteredText(drawingContext, LocalizationService.ItemCaption(item), textCenter, maxWidth,
                    _items.Count > 8 ? 10.5 : 12, textBrush, 2);
            }
        }

    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsOverlay)
        {
            return;
        }

        var point = e.GetPosition(this);
        var hit = HitTestWheel(point);
        var nextHovered = hit.Kind == HitKind.Item ? hit.Index : -1;
        var nextBack = hit.Kind == HitKind.Back;
        if (nextHovered != _hoveredIndex || nextBack != _backHovered)
        {
            _hoveredIndex = nextHovered;
            _backHovered = nextBack;
            Cursor = nextHovered >= 0 || nextBack ? Cursors.Hand : Cursors.Arrow;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoveredIndex = -1;
        _backHovered = false;
        Cursor = Cursors.Arrow;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        CaptureMouse();
        var hit = HitTestWheel(e.GetPosition(this));
        _pressedIndex = hit.Kind == HitKind.Item ? hit.Index : -1;
        _backPressed = hit.Kind == HitKind.Back;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        var hit = HitTestWheel(e.GetPosition(this));
        var pressedIndex = _pressedIndex;
        var backPressed = _backPressed;
        _pressedIndex = -1;
        _backPressed = false;
        InvalidateVisual();
        e.Handled = true;

        if (hit.Kind == HitKind.Back && backPressed)
        {
            BackActivated?.Invoke();
            return;
        }

        if (hit.Kind == HitKind.Item && hit.Index == pressedIndex)
        {
            var item = _items[hit.Index];
            if (item.Type == WheelItemType.Group)
            {
                GroupActivated?.Invoke(item);
            }
            else
            {
                ItemActivated?.Invoke(item);
            }

            return;
        }

        if (hit.Kind == HitKind.Outside)
        {
            CancelRequested?.Invoke();
        }
    }

    private void DrawCenter(DrawingContext context, WpfPoint center, double innerRadius, Brush sectorBrush,
        Brush accentBrush, Brush textBrush)
    {
        var centerRadius = innerRadius * 0.88;
        context.DrawEllipse(sectorBrush, new Pen(accentBrush, 1.4), center, centerRadius, centerRadius);
        var hovered = _backHovered && CanGoBack;
        if (CanGoBack)
        {
            var glowRadius = centerRadius + (hovered ? 2 : 0);
            var glow = new Pen(new SolidColorBrush(Color.FromArgb(90, 76, 201, 240)), hovered ? 4 : 1);
            context.DrawEllipse(null, glow, center, glowRadius, glowRadius);
            DrawCenteredText(context, LocalizationService.Get("Back"),
                new WpfPoint(center.X, center.Y), centerRadius * 1.6, 11.5,
                accentBrush, 1);
        }
        else
        {
            var title = WheelTitle.Length > 18
                ? LocalizationService.Get("MainCenter")
                : WheelTitle.ToUpperInvariant();
            DrawCenteredText(context, title, new WpfPoint(center.X, center.Y - 4), centerRadius * 1.5, 10.5,
                textBrush, 1);
            context.DrawEllipse(new SolidColorBrush(Color.FromArgb(180, 76, 201, 240)), null,
                new WpfPoint(center.X, center.Y + 14), 2.5, 2.5);
        }
    }

    private void DrawEmptyState(DrawingContext context, WpfPoint center, double innerRadius, Brush textBrush)
    {
        var radius = innerRadius * 2.4;
        var dashedPen = new Pen(CreateBrush(_theme.Accent, 0.35), 1.5)
        {
            DashStyle = new DashStyle([4, 5], 0)
        };
        dashedPen.Freeze();
        context.DrawEllipse(null, dashedPen, center, radius, radius);
        DrawCenteredText(context, LocalizationService.Get("EmptyWheel"),
            new WpfPoint(center.X, center.Y), radius, 12, textBrush, 1);
    }

    private void DrawIcon(DrawingContext context, WpfPoint center, double size, WheelItem item, Brush textBrush)
    {
        var icon = IconService.Resolve(item);
        if (icon.Image is not null)
        {
            var brush = new ImageBrush(icon.Image)
            {
                Stretch = Stretch.Uniform
            };
            brush.Freeze();
            context.DrawRectangle(brush, null, new Rect(center.X - size / 2, center.Y - size / 2, size, size));
            return;
        }

        var typeface = new Typeface(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var formatted = new FormattedText(icon.Glyph, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            typeface, size, textBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.DrawText(formatted,
            new WpfPoint(center.X - formatted.Width / 2, center.Y - formatted.Height / 2));
    }

    private void DrawCenteredText(DrawingContext context, string text, WpfPoint center, double maxWidth,
        double fontSize, Brush brush, int maxLines)
    {
        var typeface = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var flowDirection = LocalizationService.CurrentLanguage == AppLanguage.Arabic
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        var lines = WrapText(text, typeface, fontSize, maxWidth, pixelsPerDip, maxLines);
        var lineHeight = fontSize * 1.35;
        var totalHeight = lines.Count * lineHeight;
        for (var index = 0; index < lines.Count; index++)
        {
            var formatted = new FormattedText(lines[index], CultureInfo.CurrentUICulture,
                flowDirection, typeface, fontSize, brush, pixelsPerDip)
            {
                MaxTextWidth = maxWidth,
                Trimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center
            };
            context.DrawText(formatted, new WpfPoint(center.X - maxWidth / 2,
                center.Y - totalHeight / 2 + index * lineHeight));
        }
    }

    private static List<string> WrapText(string text, Typeface typeface, double fontSize, double maxWidth,
        double pixelsPerDip, int maxLines)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return [""];
        }

        var lines = new List<string>();
        var current = "";
        var flowDirection = LocalizationService.CurrentLanguage == AppLanguage.Arabic
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            var formatted = new FormattedText(candidate, CultureInfo.CurrentUICulture,
                flowDirection, typeface, fontSize, Brushes.White, pixelsPerDip);
            if (formatted.Width <= maxWidth || current.Length == 0)
            {
                current = candidate;
            }
            else
            {
                lines.Add(current);
                current = word;
                if (lines.Count == maxLines)
                {
                    break;
                }
            }
        }

        if (lines.Count < maxLines && current.Length > 0)
        {
            lines.Add(current);
        }

        if (lines.Count > maxLines)
        {
            lines = lines.Take(maxLines).ToList();
        }

        return lines;
    }

    private HitResult HitTestWheel(WpfPoint point)
    {
        var center = _fixedCenter ?? new WpfPoint(ActualWidth / 2, ActualHeight / 2);
        var maxAvailable = Math.Max(80, Math.Min(center.X, ActualWidth - center.X)
                                           .Min(center.Y, ActualHeight - center.Y) * 2 - 18);
        var outer = Math.Min(_desiredDiameter, maxAvailable) / 2;
        var hit = RadialWheelLayout.HitTest(point, center, outer, _items.Count, CanGoBack);
        return new HitResult((HitKind)hit.Kind, hit.Index);
    }

    private static Geometry CreateSectorGeometry(WpfPoint center, double innerRadius, double outerRadius,
        double startAngle, double endAngle)
    {
        var outerStart = Polar(center, outerRadius, startAngle);
        var outerEnd = Polar(center, outerRadius, endAngle);
        var innerEnd = Polar(center, innerRadius, endAngle);
        var innerStart = Polar(center, innerRadius, startAngle);
        var figure = new PathFigure
        {
            StartPoint = outerStart,
            IsClosed = true,
            IsFilled = true
        };
        figure.Segments.Add(new ArcSegment(outerEnd, new Size(outerRadius, outerRadius), 0,
            endAngle - startAngle >= 180, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(innerEnd, true));
        figure.Segments.Add(new ArcSegment(innerStart, new Size(innerRadius, innerRadius), 0,
            endAngle - startAngle >= 180, SweepDirection.Counterclockwise, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private static Geometry CreateFullRingGeometry(WpfPoint center, double innerRadius, double outerRadius)
    {
        var combined = new CombinedGeometry(
            GeometryCombineMode.Exclude,
            new EllipseGeometry(center, outerRadius, outerRadius),
            new EllipseGeometry(center, innerRadius, innerRadius));
        combined.Freeze();
        return combined;
    }

    private static WpfPoint Polar(WpfPoint center, double radius, double angle) =>
        new(center.X + radius * Math.Cos(angle * Math.PI / 180),
            center.Y + radius * Math.Sin(angle * Math.PI / 180));

    private static Brush CreateBrush(string color, double opacity)
    {
        var parsed = ParseColor(color, Colors.White);
        var brush = new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(opacity, 0, 1) * 255),
            parsed.R, parsed.G, parsed.B));
        brush.Freeze();
        return brush;
    }

    private static Color ParseColor(string color, Color fallback)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(color);
        }
        catch
        {
            return fallback;
        }
    }

    private enum HitKind
    {
        Outside,
        Item,
        Back
    }

    private readonly record struct HitResult(HitKind Kind, int Index);
}

internal static class DoubleExtensions
{
    public static double Min(this double first, double second, double third) =>
        Math.Min(first, Math.Min(second, third));
}

