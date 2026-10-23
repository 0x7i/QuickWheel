namespace RadialCommandWheel.Services;

public sealed record MonitorBounds(
    NativeMethods.Rect Monitor,
    NativeMethods.Rect WorkArea,
    NativeMethods.Point Cursor)
{
    public int Dpi { get; init; } = 96;
}

public static class MonitorService
{
    public static MonitorBounds GetMonitorAtCursor()
    {
        NativeMethods.GetCursorPos(out var cursor);
        var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            throw new InvalidOperationException("Windows could not report the monitor containing the pointer.");
        }

        return new MonitorBounds(info.Monitor, info.Work, cursor);
    }
}

