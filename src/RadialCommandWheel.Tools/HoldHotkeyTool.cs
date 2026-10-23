using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RadialCommandWheel.Tools;

internal static class HoldHotkeyTool
{
    private const int HotkeyId = 0x7F01;
    private const uint ModNoRepeat = 0x4000;
    private const uint CtrlAndAlt = 0x0003;
    private const uint Space = 0x20;

    public static int Run(string[] args)
    {
        var (markerPath, seconds) = ParseArguments(args);
        if (!RegisterHotKey(IntPtr.Zero, HotkeyId, ModNoRepeat | CtrlAndAlt, Space))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterHotKey failed.");
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(markerPath))
            {
                markerPath = Path.GetFullPath(markerPath);
                Directory.CreateDirectory(Path.GetDirectoryName(markerPath)
                                          ?? throw new InvalidOperationException(
                                              "The marker path has no directory."));
                File.WriteAllText(markerPath, "ready");
            }

            Thread.Sleep(TimeSpan.FromSeconds(seconds));
            return 0;
        }
        finally
        {
            UnregisterHotKey(IntPtr.Zero, HotkeyId);
        }
    }

    private static (string? MarkerPath, int Seconds) ParseArguments(string[] args)
    {
        string? markerPath = null;
        var seconds = 30;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--marker":
                case "-m":
                    if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                    {
                        throw new ArgumentException("--marker requires a path.");
                    }

                    markerPath = args[index];
                    break;
                case "--seconds":
                case "-s":
                    if (++index >= args.Length ||
                        !int.TryParse(args[index], out seconds) ||
                        seconds is < 1 or > 3600)
                    {
                        throw new ArgumentException("--seconds must be an integer from 1 to 3600.");
                    }

                    break;
                default:
                    throw new ArgumentException($"Unknown hold-hotkey option: {args[index]}");
            }
        }

        return (markerPath, seconds);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}

