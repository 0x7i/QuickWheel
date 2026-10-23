using System.Threading;
using System.Windows;
using Application = System.Windows.Application;
using RadialCommandWheel.Core;
using RadialCommandWheel.Services;

namespace RadialCommandWheel;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LocalizationService.ApplyLanguage(new ConfigurationStore().ReadLanguageHint());
        DispatcherUnhandledException += (_, eventArgs) =>
            AppLog.Write("Unhandled UI exception.", eventArgs.Exception);
        _singleInstanceMutex = new Mutex(true, "RadialCommandWheel.SingleInstance.7B8D1C9F", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(LocalizationService.Get("AlreadyRunning"),
                LocalizationService.Get("WindowTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            AppLog.Write($"Application startup. Version={typeof(App).Assembly.GetName().Version}; Args={string.Join(' ', e.Args)}");
            _controller = new AppController();
            _controller.Initialize(startInBackground: e.Args.Contains("--background",
                StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception exception)
        {
            AppLog.Write("Application startup failed.", exception);
            MessageBox.Show($"{LocalizationService.Get("CouldNotStart")}\n\n{exception.Message}",
                LocalizationService.Get("StartupFailure"), MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}

