using Microsoft.Win32;

namespace RadialCommandWheel.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RadialCommandWheel";

    public string? ReadRegisteredCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return key?.GetValue(ValueName) as string;
    }

    public void SetEnabled(bool enabled)
    {
        AppLog.Write($"Updating Windows startup registration. Enabled={enabled}; Executable={Environment.ProcessPath}");
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
        if (!enabled)
        {
            if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, false);
                AppLog.Write("Removed the Windows startup registration.");
            }

            return;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) ||
            !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The application executable path is unavailable for startup registration.");
        }

        var command = $"\"{executable}\" --background";
        key.SetValue(ValueName, command, RegistryValueKind.String);
        AppLog.Write($"Windows startup command set to {command}; immediate read-back={key.GetValue(ValueName)}");
    }
}

