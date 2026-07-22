using System;
using System.IO;
using System.Threading;
using System.Windows;
using CefSharp;
using CefSharp.Wpf;
using FocusLock.Config;
using FocusLock.Core;
using FocusLock.Logging;

namespace FocusLock.App;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private LoggingService? _loggingService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        const string mutexName = "Global\\FocusLockBrowserSingleInstanceMutex";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool isNewInstance);
        if (!isNewInstance)
        {
            MessageBox.Show("FlyLock Browser is already running.", "FlyLock Browser", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            _loggingService?.Log("UnhandledAppDomainException", new { error = ex?.ToString(), isTerminating = args.IsTerminating });
        };

        DispatcherUnhandledException += (s, args) =>
        {
            _loggingService?.Log("UnhandledDispatcherException", new { error = args.Exception.ToString() });
            args.Handled = true;
        };

        var configService = new ConfigService();
        FocusLockConfig config;
        try
        {
            config = configService.Load(e.Args);
        }
        catch (Exception ex)
        {
            string emergencySessionId = Guid.NewGuid().ToString("N");
            using var emergencyLogger = new LoggingService(emergencySessionId);
            emergencyLogger.Log("ConfigLoadFailed", new { error = ex.Message, args = e.Args });

            MessageBox.Show($"FocusLock Configuration Error:\n\n{ex.Message}", "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(2);
            return;
        }

        var stateMachine = new SessionStateMachine(
            config.FocusMonitoring.WarningThreshold,
            config.FocusMonitoring.TerminationThreshold,
            config.FocusMonitoring.FocusLossDebounceMs
        );

        _loggingService = new LoggingService(stateMachine.SessionId, config.Logging.LogDirectory, config.Logging.HashChain);
        _loggingService.Log("SessionStarted", new
        {
            sessionId = stateMachine.SessionId,
            examUrl = config.ExamUrl,
            configPath = configService.ConfigPathUsed,
            warningThreshold = config.FocusMonitoring.WarningThreshold,
            terminationThreshold = config.FocusMonitoring.TerminationThreshold
        });

        InitializeCef(config);

        var mainWindow = new MainWindow(config, _loggingService, stateMachine);
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private static void InitializeCef(FocusLockConfig config)
    {
        var settings = new CefSettings();

        string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusLock", "CefCache");
        settings.CachePath = appDataPath;

        settings.CefCommandLineArgs.Add("disable-devtools", "1");
        settings.CefCommandLineArgs.Add("disable-extensions", "1");

        Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cef.Shutdown();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
