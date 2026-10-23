using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using RadialCommandWheel.Core;
using RadialCommandWheel.Services;
using RadialCommandWheel.UI;
using Forms = System.Windows.Forms;

namespace RadialCommandWheel;

public sealed class AppController : IDisposable
{
    private readonly ConfigurationStore _store = new();
    private readonly LaunchService _launcher = new();
    private readonly StartupService _startup = new();
    private readonly UndoHistory _undo = new(40);
    private GlobalShortcutService? _shortcuts;
    private TrayService? _tray;
    private MainWindow? _management;
    private OverlayWindow? _overlay;
    private WheelItem? _currentGroup;
    private bool _isExiting;
    private bool _disposed;

    public AppConfiguration Configuration { get; private set; } = ConfigurationDefaults.Create();
    public string ConfigurationPath => _store.Path;
    public string ShortcutStatus { get; private set; } = "";
    public bool ShortcutRegistered => _shortcuts?.IsRegistered == true;
    public bool CanUndo => _undo.CanUndo;
    public string LocalizedShortcutStatus
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ShortcutStatus))
            {
                return "";
            }

            if (ShortcutStatus.Contains("already registered", StringComparison.OrdinalIgnoreCase))
            {
                return LocalizationService.Get("ShortcutConflict");
            }

            if (ShortcutStatus.Contains("denied", StringComparison.OrdinalIgnoreCase))
            {
                return LocalizationService.Get("ShortcutDenied");
            }

            return LocalizationService.Get("ShortcutRegistrationFailed");
        }
    }

    public void Initialize(bool startInBackground)
    {
        var load = _store.Load();
        Configuration = load.Configuration;
        ConfigurationValidator.Normalize(Configuration);
        LocalizationService.ApplyLanguage(Configuration.Settings.Language);
        _management = new MainWindow(this);
        new System.Windows.Interop.WindowInteropHelper(_management).EnsureHandle();
        _management.Closing += ManagementOnClosing;

        _shortcuts = new GlobalShortcutService();
        _shortcuts.Initialize(_management);
        _shortcuts.Activated += () => Application.Current.Dispatcher.BeginInvoke(ShowWheel);
        _shortcuts.Cancelled += () => Application.Current.Dispatcher.BeginInvoke(CancelWheel);

        _tray = new TrayService(ShowManagement, ShowWheel, ExitApplication);
        RegisterShortcut();
        TryApplyStartupSetting();

        if (!startInBackground)
        {
            ShowManagement();
        }

        if (!string.IsNullOrWhiteSpace(load.Message) &&
            (load.RecoveredFromBackup || load.Message.Contains("could not", StringComparison.OrdinalIgnoreCase)))
        {
            AppLog.Write(load.Message);
            Notify(LocalizationService.Get("ConfigurationRecovered"),
                LocalizationService.Get(load.RecoveredFromBackup
                    ? "ConfigurationRecoveryBackup"
                    : "ConfigurationRecoveryDefaults"),
                Forms.ToolTipIcon.Warning);
        }
    }

    public void ShowManagement()
    {
        if (_management is null)
        {
            return;
        }

        _management.Show();
        if (_management.WindowState == WindowState.Minimized)
        {
            _management.WindowState = WindowState.Normal;
        }

        _management.Activate();
        _management.Topmost = true;
        _management.Topmost = false;
        _management.Focus();
    }

    public void ShowWheel()
    {
        if (_isExiting || _overlay is not null)
        {
            return;
        }

        try
        {
            var monitor = MonitorService.GetMonitorAtCursor();
            _currentGroup = null;
            var overlay = new OverlayWindow(monitor, Configuration.ActiveTheme, Configuration.MainWheel);
            _overlay = overlay;
            overlay.ItemInvoked += InvokeItem;
            overlay.GroupInvoked += OpenGroup;
            overlay.BackRequested += GoBack;
            overlay.CancelRequested += CancelWheel;
            overlay.Closed += (_, _) =>
            {
                if (ReferenceEquals(_overlay, overlay))
                {
                    _overlay = null;
                    _shortcuts?.SetWheelActive(false);
                }
            };
            overlay.Show();
            _shortcuts?.SetWheelActive(true);
        }
        catch (Exception exception)
        {
            _shortcuts?.SetWheelActive(false);
            Notify(LocalizationService.Get("CouldNotShowWheel"), exception.Message, Forms.ToolTipIcon.Error);
        }
    }

    public void CancelWheel()
    {
        _shortcuts?.SetWheelActive(false);
        CloseOverlay();
    }

    public void RecordUndo() => _undo.Record(Configuration);

    public bool UndoLastChange()
    {
        var restored = _undo.Undo();
        if (restored is null)
        {
            return false;
        }

        Configuration = restored;
        SaveNow();
        _management?.ReloadConfiguration();
        return true;
    }

    public void SaveNow()
    {
        ConfigurationValidator.Normalize(Configuration);
        try
        {
            _store.Save(Configuration);
            _management?.ShowSaveStatus(LocalizationService.Get("SavedStatus"), false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _management?.ShowSaveStatus(
                LocalizationService.Format("SaveFailedFormat", exception.Message), true);
            Notify(LocalizationService.Get("CouldNotSaveSettings"), exception.Message,
                Forms.ToolTipIcon.Error);
        }
    }

    public string RegisterShortcut()
    {
        if (_shortcuts is null)
        {
            return LocalizationService.Get("ShortcutServiceNotReady");
        }

        var registered = _shortcuts.Register(Configuration.Shortcut);
        ShortcutStatus = _shortcuts.LastError;
        AppLog.Write($"Shortcut registration for {Configuration.Shortcut.DisplayText}: " +
                     (registered ? "succeeded" : ShortcutStatus));
        _management?.UpdateShortcutStatus(registered);
        return ShortcutStatus;
    }

    public void UnregisterShortcut()
    {
        _shortcuts?.Unregister();
        _shortcuts?.SetWheelActive(false);
    }

    public void TryApplyStartupSetting()
    {
        try
        {
            _startup.SetEnabled(Configuration.Settings.StartWithWindows);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException
                                               or InvalidOperationException)
        {
            AppLog.Write("Startup setting could not be applied.", exception);
            Notify(LocalizationService.Get("StartupNotApplied"), exception.Message,
                Forms.ToolTipIcon.Warning);
        }
    }

    public string? GetStartupRegistration() => _startup.ReadRegisteredCommand();

    public void OpenConfigurationFolder()
    {
        var directory = Path.GetDirectoryName(ConfigurationPath);
        if (directory is not null)
        {
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
    }

    public void Notify(string title, string message, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info)
    {
        if (!Configuration.Settings.ShowTrayNotifications && icon == Forms.ToolTipIcon.Info)
        {
            return;
        }

        _tray?.Notify(title, message, icon);
    }

    public void ApplyLanguage(AppLanguage language)
    {
        Configuration.Settings.Language = language;
        LocalizationService.ApplyLanguage(language);
        _tray?.RefreshLanguage();
        _management?.ApplyLanguage();
        QueueDeferredSave();
    }

    public ImportCommitResult CommitImportedConfiguration(AppConfiguration imported)
    {
        var previous = Configuration;
        string backupPath;
        try
        {
            backupPath = _store.CreateRecoveryBackup("pre-import");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ImportCommitResult(false, "", false, 0, exception.Message);
        }

        _undo.Record(previous);
        Configuration = imported.Clone();
        ConfigurationValidator.Normalize(Configuration);
        var validation = ConfigurationValidator.Validate(Configuration);
        if (!validation.IsValid)
        {
            Configuration = previous;
            return new ImportCommitResult(false, backupPath, false, 0,
                string.Join(Environment.NewLine, validation.Errors));
        }

        try
        {
            _store.Save(Configuration);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Configuration = previous;
            return new ImportCommitResult(false, backupPath, false, 0, exception.Message);
        }

        CloseOverlay();
        LocalizationService.ApplyLanguage(Configuration.Settings.Language);
        _tray?.RefreshLanguage();
        _management?.ReloadConfiguration();
        _management?.ApplyLanguage();
        RegisterShortcut();
        var missing = ConfigurationValidator.EnumerateAll(Configuration.MainWheel)
            .Where(item => item.Type != WheelItemType.Group)
            .Count(item => TargetValidator.Validate(item).IsError);
        AppLog.Write($"Imported configuration. Backup={backupPath}; MissingTargets={missing}; " +
                     $"ShortcutRegistered={ShortcutRegistered}");
        return new ImportCommitResult(true, backupPath, ShortcutRegistered, missing, null);
    }

    private void QueueDeferredSave()
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            SaveNow();
            _management?.ShowSaveStatus(LocalizationService.Get("SavedStatus"), false);
        });
    }

    public void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        SaveNow();
        _shortcuts?.Unregister();
        CloseOverlay();
        _tray?.Dispose();
        _tray = null;
        _management?.Close();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shortcuts?.Dispose();
        _tray?.Dispose();
        _overlay?.Close();
    }

    private void InvokeItem(WheelItem item)
    {
        if (_shortcuts?.AreRequiredChordKeysDown() != true)
        {
            CancelWheel();
            return;
        }

        _shortcuts?.CommitSelection();
        CloseOverlay();
        _ = Task.Run(() =>
        {
            var result = _launcher.Launch(item);
            if (!result.Success)
            {
                AppLog.Write($"Launch failed: {result.Message}");
                Notify(LocalizationService.Get("LaunchFailed"), LocalizeLaunchMessage(result),
                    Forms.ToolTipIcon.Error);
            }
            else if (!result.ActivatedExistingWindow && Configuration.Settings.ShowTrayNotifications)
            {
                Notify(LocalizationService.ItemCaption(item), LocalizeLaunchMessage(result));
            }
        });
    }

    private void OpenGroup(WheelItem group)
    {
        if (_overlay is null || _currentGroup is not null || _shortcuts?.AreRequiredChordKeysDown() != true)
        {
            CancelWheel();
            return;
        }

        _currentGroup = group;
        _overlay.SetWheel(LocalizationService.ItemCaption(group), group.Children, true);
    }

    private void GoBack()
    {
        if (_overlay is null || _shortcuts?.AreRequiredChordKeysDown() != true)
        {
            CancelWheel();
            return;
        }

        _currentGroup = null;
        _overlay.SetWheel(LocalizationService.Get("MainCenter"), Configuration.MainWheel, false);
    }

    private void CloseOverlay()
    {
        var overlay = _overlay;
        _overlay = null;
        _currentGroup = null;
        if (overlay is not null)
        {
            overlay.Close();
        }
    }

    private void ManagementOnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        e.Cancel = true;
        _management?.Hide();
        if (Configuration.Settings.ShowTrayNotifications)
        {
            Notify(LocalizationService.Get("StillRunningTitle"),
                LocalizationService.Get("StillRunningMessage"));
        }
    }

    private static string LocalizeLaunchMessage(LaunchResult result) => result.MessageKey switch
    {
        "LaunchActivated" => LocalizationService.Format("LaunchActivated", result.MessageArguments ?? []),
        "LaunchSucceeded" => LocalizationService.Format("LaunchSucceeded", result.MessageArguments ?? []),
        "LaunchMissingFile" => LocalizationService.Format("LaunchMissingFile", result.MessageArguments ?? []),
        "LaunchMissingTarget" => LocalizationService.Format("LaunchMissingTarget", result.MessageArguments ?? []),
        "LaunchEmpty" => LocalizationService.Get("LaunchEmpty"),
        "LaunchGroup" => LocalizationService.Get("LaunchGroup"),
        "LaunchUnsupported" => LocalizationService.Get("LaunchUnsupported"),
        "LaunchFailure" => LocalizationService.Format("LaunchFailure", result.MessageArguments ?? []),
        _ => result.Message
    };
}

public sealed record ImportCommitResult(
    bool Success,
    string BackupPath,
    bool ShortcutRegistered,
    int MissingTargetCount,
    string? ErrorMessage);

