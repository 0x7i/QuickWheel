using WpfPoint = System.Windows.Point;

namespace RadialCommandWheel.UI;

public enum WheelHitKind
{
    Outside,
    Item,
    Back
}

public readonly record struct WheelHit(WheelHitKind Kind, int Index);

public readonly record struct RadialWheelMetrics(
    double OuterRadius,
    double InnerRadius,
    double GapDegrees,
    double SweepDegrees,
    bool IsSingleItem,
    double IconRadius,
    double LabelRadius,
    double CenterRadius);

public static class RadialWheelLayout
{
    public static RadialWheelMetrics CalculateMetrics(double outerRadius, int itemCount)
    {
        var safeCount = Math.Max(1, itemCount);
        var innerRatio = safeCount switch
        {
            1 => 0.27,
            2 => 0.29,
            3 => 0.31,
            <= 4 => 0.325,
            _ => 0.34
        };
        var sweep = 360.0 / safeCount;
        var gap = safeCount == 1
            ? 0
            : Math.Clamp(0.85 + 2.4 / safeCount, 1.05, 2.55);
        var inner = outerRadius * innerRatio;
        var iconRatio = safeCount == 1 ? 0.70 : 0.72;
        var labelRatio = safeCount switch
        {
            1 => 0.48,
            2 => 0.50,
            <= 4 => 0.49,
            _ => 0.47
        };
        return new RadialWheelMetrics(
            outerRadius,
            inner,
            gap,
            sweep,
            safeCount == 1,
            inner + (outerRadius - inner) * iconRatio,
            inner + (outerRadius - inner) * labelRatio,
            inner * 0.88);
    }

    public static WheelHit HitTest(WpfPoint point, WpfPoint center, double outerRadius,
        double itemCount, bool canGoBack)
    {
        var metrics = CalculateMetrics(outerRadius, (int)itemCount);
        var innerRadius = metrics.InnerRadius;
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance <= innerRadius * 0.9 && canGoBack)
        {
            return new WheelHit(WheelHitKind.Back, -1);
        }

        if (distance < innerRadius || distance > outerRadius + 8 || itemCount < 1)
        {
            return new WheelHit(WheelHitKind.Outside, -1);
        }

        var angle = Math.Atan2(dy, dx) * 180 / Math.PI;
        var fromTop = (angle + 90 + 360) % 360;
        var index = (int)(fromTop / (360.0 / itemCount));
        return new WheelHit(WheelHitKind.Item,
            Math.Clamp(index, 0, Math.Max(0, (int)itemCount - 1)));
    }
}

