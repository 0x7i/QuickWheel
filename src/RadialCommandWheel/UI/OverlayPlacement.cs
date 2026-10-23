using WpfPoint = System.Windows.Point;

namespace RadialCommandWheel.UI;

public readonly record struct OverlayPlacementResult(WpfPoint Center, double Diameter);

public static class OverlayPlacement
{
    public static OverlayPlacementResult Calculate(int workAreaWidth, int workAreaHeight, int cursorX, int cursorY,
        double viewportWidth, double viewportHeight, double configuredDiameter)
    {
        var physicalX = Math.Clamp(cursorX, 0, Math.Max(0, workAreaWidth - 1));
        var physicalY = Math.Clamp(cursorY, 0, Math.Max(0, workAreaHeight - 1));
        var centerX = physicalX * viewportWidth / Math.Max(1, workAreaWidth);
        var centerY = physicalY * viewportHeight / Math.Max(1, workAreaHeight);
        var diameter = Math.Min(configuredDiameter, Math.Min(viewportWidth, viewportHeight) * 0.94);
        var radius = diameter / 2;
        const double margin = 14;
        var safeHalf = Math.Max(0, radius + margin);
        centerX = viewportWidth > safeHalf * 2
            ? Math.Clamp(centerX, safeHalf, viewportWidth - safeHalf)
            : viewportWidth / 2;
        centerY = viewportHeight > safeHalf * 2
            ? Math.Clamp(centerY, safeHalf, viewportHeight - safeHalf)
            : viewportHeight / 2;
        return new OverlayPlacementResult(new WpfPoint(centerX, centerY), diameter);
    }
}

