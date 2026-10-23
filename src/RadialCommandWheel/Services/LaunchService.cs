using System.ComponentModel;
using System.Diagnostics;
using RadialCommandWheel.Core;

namespace RadialCommandWheel.Services;

public sealed record LaunchResult(
    bool Success,
    bool ActivatedExistingWindow,
    string Message,
    string MessageKey = "",
    string[]? MessageArguments = null);

public sealed class LaunchService
{
    public LaunchResult Launch(WheelItem item)
    {
        try
        {
            return item.Type switch
            {
                WheelItemType.Application => LaunchApplication(item),
                WheelItemType.File => OpenPath(item.Target, "file"),
                WheelItemType.Folder => OpenPath(item.Target, "folder"),
                WheelItemType.Command => LaunchCommand(item),
                WheelItemType.Group => new LaunchResult(false, false,
                    "A group cannot be executed as an item.", "LaunchGroup"),
                _ => new LaunchResult(false, false, "The item type is not supported.", "LaunchUnsupported")
            };
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException
                                               or UnauthorizedAccessException or FormatException)
        {
            return new LaunchResult(false, false,
                $"Could not launch '{item.DisplayCaption}': {exception.Message}",
                "LaunchFailure", [item.DisplayCaption, exception.Message]);
        }
    }

    private static LaunchResult LaunchApplication(WheelItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Target))
        {
            return new LaunchResult(false, false, "The application path is empty.", "LaunchEmpty");
        }

        if (File.Exists(item.Target) && TryActivateExisting(item.Target))
        {
            return new LaunchResult(true, true,
                $"Activated the existing window for '{item.DisplayCaption}'.",
                "LaunchActivated", [item.DisplayCaption]);
        }

        if (Path.IsPathFullyQualified(item.Target) && !File.Exists(item.Target))
        {
            return new LaunchResult(false, false, $"The application file is missing: {item.Target}",
                "LaunchMissingFile", [item.Target]);
        }

        var startInfo = new ProcessStartInfo(item.Target)
        {
            UseShellExecute = true,
            WorkingDirectory = ResolveWorkingDirectory(item.Target),
            Arguments = item.Arguments
        };
        Process.Start(startInfo);
        return new LaunchResult(true, false, $"Launched '{item.DisplayCaption}'.",
            "LaunchSucceeded", [item.DisplayCaption]);
    }

    private static LaunchResult OpenPath(string path, string kind)
    {
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
        {
            return new LaunchResult(false, false, $"The {kind} is missing: {path}",
                "LaunchMissingTarget", [path]);
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return new LaunchResult(true, false, $"Opened {kind} '{Path.GetFileName(path.TrimEnd('\\', '/'))}'.",
            "LaunchSucceeded",
            [DisplayNameHelper.FromTarget(path,
                kind == "folder" ? WheelItemType.Folder : WheelItemType.File)]);
    }

    private static LaunchResult LaunchCommand(WheelItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Target))
        {
            return new LaunchResult(false, false, "The command executable is empty.", "LaunchEmpty");
        }

        var arguments = WindowsArgumentParser.Split(item.Arguments);
        var extension = Path.GetExtension(item.Target).ToLowerInvariant();
        var requiresShell = extension is ".bat" or ".cmd" or ".ps1" or ".vbs";
        if (!requiresShell && Path.IsPathFullyQualified(item.Target) && !File.Exists(item.Target))
        {
            return new LaunchResult(false, false, $"The command executable is missing: {item.Target}",
                "LaunchMissingFile", [item.Target]);
        }

        if (requiresShell)
        {
            var startInfo = new ProcessStartInfo(item.Target)
            {
                UseShellExecute = true,
                WorkingDirectory = ResolveWorkingDirectory(item.Target),
                Arguments = WindowsArgumentParser.Join(arguments)
            };
            Process.Start(startInfo);
        }
        else
        {
            var startInfo = new ProcessStartInfo(item.Target)
            {
                UseShellExecute = false,
                WorkingDirectory = ResolveWorkingDirectory(item.Target),
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            try
            {
                Process.Start(startInfo);
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 740)
            {
                // Let Windows show its normal UAC prompt instead of failing an otherwise valid command.
                startInfo.UseShellExecute = true;
                startInfo.Arguments = WindowsArgumentParser.Join(arguments);
                Process.Start(startInfo);
            }
        }

        return new LaunchResult(true, false, $"Executed '{item.DisplayCaption}'.",
            "LaunchSucceeded", [item.DisplayCaption]);
    }

    private static bool TryActivateExisting(string executablePath)
    {
        IntPtr matchedWindow = IntPtr.Zero;
        NativeMethods.EnumWindows((window, _) =>
        {
            if (!NativeMethods.IsWindowVisible(window) || NativeMethods.GetWindowTextLength(window) == 0)
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(window, out var processId);
            if (processId == 0)
            {
                return true;
            }

            try
            {
                using var process = Process.GetProcessById((int)processId);
                if (process.MainWindowHandle != IntPtr.Zero &&
                    string.Equals(process.MainModule?.FileName, executablePath, StringComparison.OrdinalIgnoreCase))
                {
                    matchedWindow = window;
                    return false;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
                                                   or Win32Exception)
            {
                // Protected processes can fail inspection; continue looking for an accessible window.
            }

            return true;
        }, IntPtr.Zero);

        if (matchedWindow == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.ShowWindowAsync(matchedWindow, NativeMethods.SwRestore);
        NativeMethods.BringWindowToTop(matchedWindow);
        if (NativeMethods.SetForegroundWindow(matchedWindow))
        {
            return true;
        }

        var foregroundWindow = NativeMethods.GetForegroundWindow();
        var foregroundThread = foregroundWindow == IntPtr.Zero
            ? 0
            : NativeMethods.GetWindowThreadProcessId(foregroundWindow, IntPtr.Zero);
        var currentThread = NativeMethods.GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != currentThread &&
                       NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            NativeMethods.ShowWindowAsync(matchedWindow, NativeMethods.SwRestore);
            return NativeMethods.SetForegroundWindow(matchedWindow);
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    private static string ResolveWorkingDirectory(string target)
    {
        try
        {
            return Path.GetDirectoryName(Path.GetFullPath(target)) ?? Environment.CurrentDirectory;
        }
        catch
        {
            return Environment.CurrentDirectory;
        }
    }
}

