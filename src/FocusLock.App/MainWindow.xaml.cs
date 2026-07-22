using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using FocusLock.App.Views;
using FocusLock.Browser;
using FocusLock.Config;
using FocusLock.Core;
using FocusLock.Focus;
using FocusLock.Logging;
using FocusLock.Security;

namespace FocusLock.App;

public partial class MainWindow : Window
{
    private readonly FocusLockConfig _config;
    private readonly LoggingService _loggingService;
    private readonly SessionStateMachine _stateMachine;
    private readonly ExitAuthorizationService _authService;
    private FocusMonitorService? _focusMonitor;
    private LowLevelKeyboardHook? _keyboardHook;
    private TouchpadManager? _touchpadManager;
    private BrowserHostControl? _browserHost;
    private bool _isExiting = false;

    private readonly HomeView _homeView;
    private readonly LoadingView _loadingView;
    private readonly TerminatedView _terminatedView;
    private readonly CompletionView _completionView;
    private readonly ErrorView _errorView;

    public MainWindow(FocusLockConfig config, LoggingService loggingService, SessionStateMachine stateMachine)
    {
        InitializeComponent();

        _config = config ?? throw new ArgumentNullException(nameof(config));
        _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));

        _authService = new ExitAuthorizationService(
            _config.ExitAuthorization.PasswordHash,
            _config.ExitAuthorization.KeySequence
        );

        Title = _config.Ui.Branding.AppName;
        Topmost = false;
        WindowStyle = WindowStyle.None;
        WindowState = WindowState.Maximized;

        _homeView = new HomeView();
        _homeView.SetAppName(_config.Ui.Branding.AppName);
        _homeView.CodeSubmitted += (s, code) => StartExamFromHome(code);

        _loadingView = new LoadingView();
        _terminatedView = new TerminatedView();
        _completionView = new CompletionView();
        _errorView = new ErrorView();

        _loadingView.UpdateStatus("Loading assessment URL...", _config.Ui.Branding.AppName);
        _completionView.ExitRequested += (s, e) => CloseApplication("User requested exit on completion screen");
        _errorView.ExitRequested += (s, e) => CloseApplication("Operator requested exit on error screen");

        _stateMachine.StateChanged += OnStateChanged;

        Loaded += MainWindow_Loaded;
        Activated += MainWindow_Activated;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            Win32FocusInterop.DisableWindowGestures(hwnd);
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        }

        _focusMonitor = new FocusMonitorService(this, _stateMachine, _config.FocusMonitoring.FocusLossDebounceMs);
        _focusMonitor.IsPaused = true;

        try
        {
            _touchpadManager = new TouchpadManager();
            _touchpadManager.DisableGestures();
        }
        catch (Exception ex)
        {
            _loggingService.Log("TouchpadDisableError", new { error = ex.Message });
        }

        try
        {
            _keyboardHook = new LowLevelKeyboardHook();
            _keyboardHook.BlockedKeyCombination += (s, combo) =>
            {
                _loggingService.Log("BlockedKeyCombination", new { shortcut = combo });
            };
            _keyboardHook.Start();
        }
        catch (Exception ex)
        {
            _loggingService.Log("KeyboardHookError", new { error = ex.Message });
        }

        OverlayContainer.Content = _homeView;
        OverlayContainer.Visibility = Visibility.Visible;
        BrowserContainer.Visibility = Visibility.Collapsed;
        BrowserContainer.IsHitTestVisible = false;

        try
        {
            _browserHost = new BrowserHostControl(_config, _loggingService);
            _browserHost.PageLoaded += BrowserHost_PageLoaded;
            _browserHost.PageLoadFailed += BrowserHost_PageLoadFailed;
            _browserHost.EscapeKeyPressed += (s, ev) => Dispatcher.Invoke(PromptProctorExit);

            BrowserContainer.Content = _browserHost;
        }
        catch (Exception ex)
        {
            _loggingService.Log("CefInitError", new { error = ex.ToString() });
            ShowError($"Failed to initialize Chromium Browser engine: {ex.Message}");
        }
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        if (_stateMachine.CurrentState == SessionState.Idle)
        {
            Topmost = false;
            ReassertTopmost();
            _homeView.FocusAccessCodeInput();
        }
    }

    private void StartExamFromHome(string accessCode)
    {
        _loggingService.Log("ExamCodeSubmitted", new { code = accessCode });
        OverlayContainer.Content = _loadingView;
        
        if (_focusMonitor != null)
        {
            _focusMonitor.IsPaused = false;
        }

        _stateMachine.TransitionTo(SessionState.Launching, $"Access code submitted ({accessCode})");
        _browserHost?.LoadUrl(_config.ExamUrl);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32FocusInterop.WM_GESTURE || msg == Win32FocusInterop.WM_GESTURENOTIFY)
        {
            handled = true;
            return IntPtr.Zero;
        }
        return IntPtr.Zero;
    }

    private void BrowserHost_PageLoaded(object? sender, EventArgs e)
    {
        if (_stateMachine.CurrentState == SessionState.Launching)
        {
            _stateMachine.TransitionTo(SessionState.Active, "Exam page loaded successfully");
        }
    }

    private void BrowserHost_PageLoadFailed(object? sender, string errorMessage)
    {
        if (_stateMachine.CurrentState == SessionState.Launching)
        {
            ShowError(errorMessage);
        }
    }

    private void OnStateChanged(object? sender, StateChangedEvent e)
    {
        Dispatcher.Invoke(() =>
        {
            _loggingService.Log("SessionStateChanged", new
            {
                oldState = e.OldState.ToString(),
                newState = e.NewState.ToString(),
                reason = e.Reason
            });

            switch (e.NewState)
            {
                case SessionState.Active:
                    OverlayContainer.Visibility = Visibility.Collapsed;
                    BrowserContainer.Visibility = Visibility.Visible;
                    BrowserContainer.IsHitTestVisible = true;
                    ReassertTopmost();
                    break;

                case SessionState.Warning:
                    if (_focusMonitor != null) _focusMonitor.IsPaused = true;
                    BrowserContainer.Visibility = Visibility.Collapsed;
                    BrowserContainer.IsHitTestVisible = false;

                    var warningDialog = new WarningDialog(_stateMachine.FocusLossCount, _stateMachine.TerminationThreshold)
                    {
                        Owner = this,
                        Topmost = true
                    };

                    warningDialog.ShowDialog();

                    BrowserContainer.Visibility = Visibility.Visible;
                    BrowserContainer.IsHitTestVisible = true;
                    if (_focusMonitor != null) _focusMonitor.IsPaused = false;
                    _stateMachine.TransitionTo(SessionState.Active, "User dismissed focus loss warning dialog");
                    ReassertTopmost();
                    break;

                case SessionState.Terminated:
                    Topmost = false;
                    if (_focusMonitor != null) _focusMonitor.IsPaused = true;
                    BrowserContainer.Visibility = Visibility.Collapsed;
                    BrowserContainer.IsHitTestVisible = false;
                    _browserHost?.TerminateSession();

                    _stateMachine.ResetSession();
                    _homeView.ShowTerminationNotice($"Session terminated: Focus loss limit reached ({_config.FocusMonitoring.TerminationThreshold}/{_config.FocusMonitoring.TerminationThreshold}). Re-enter access code to restart.");
                    OverlayContainer.Content = _homeView;
                    OverlayContainer.Visibility = Visibility.Visible;
                    OverlayContainer.IsHitTestVisible = true;
                    ReassertTopmost();
                    _homeView.FocusAccessCodeInput();
                    break;

                case SessionState.Completed:
                    Topmost = false;
                    if (_focusMonitor != null) _focusMonitor.IsPaused = true;
                    BrowserContainer.Visibility = Visibility.Collapsed;
                    BrowserContainer.IsHitTestVisible = false;
                    OverlayContainer.Content = _completionView;
                    OverlayContainer.Visibility = Visibility.Visible;
                    ReassertTopmost();
                    break;

                case SessionState.Error:
                    Topmost = false;
                    if (_focusMonitor != null) _focusMonitor.IsPaused = true;
                    BrowserContainer.Visibility = Visibility.Collapsed;
                    BrowserContainer.IsHitTestVisible = false;
                    OverlayContainer.Content = _errorView;
                    OverlayContainer.Visibility = Visibility.Visible;
                    ReassertTopmost();
                    break;

                case SessionState.Exited:
                    CloseApplication(e.Reason);
                    break;
            }
        });
    }

    public void ShowError(string details)
    {
        _stateMachine.ReportError(details);
        _errorView.SetError(details);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
        bool alt = Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt);
        bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
        string keyName = e.Key.ToString();

        if (e.Key == Key.Escape || _authService.IsExitShortcut(ctrl, alt, shift, keyName))
        {
            PromptProctorExit();
            e.Handled = true;
        }
    }

    private void PromptProctorExit()
    {
        if (_focusMonitor != null) _focusMonitor.IsPaused = true;

        var preDialogBrowserVisibility = BrowserContainer.Visibility;
        BrowserContainer.Visibility = Visibility.Collapsed;
        BrowserContainer.IsHitTestVisible = false;

        var dialog = new ExitDialog(_authService)
        {
            Owner = this,
            Topmost = true
        };

        bool? result = dialog.ShowDialog();

        if (result == true && dialog.IsAuthenticated)
        {
            _loggingService.Log("ProctorExitAuthenticated", new { sessionId = _stateMachine.SessionId });
            _stateMachine.AuthorizeExit("Proctor exit sequence authenticated");
        }
        else
        {
            BrowserContainer.Visibility = preDialogBrowserVisibility;
            BrowserContainer.IsHitTestVisible = (preDialogBrowserVisibility == Visibility.Visible);

            _loggingService.Log("ProctorExitFailed", new { sessionId = _stateMachine.SessionId });
            if (_focusMonitor != null && _stateMachine.CurrentState == SessionState.Active)
            {
                _focusMonitor.IsPaused = false;
            }
            ReassertTopmost();
        }
    }

    private void ReassertTopmost()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            Win32FocusInterop.ForceForegroundWindow(hwnd);
        }

        if (WindowState != WindowState.Maximized)
        {
            WindowState = WindowState.Maximized;
        }

        if (_stateMachine.CurrentState == SessionState.Active || _stateMachine.CurrentState == SessionState.Warning)
        {
            Topmost = false;
            Topmost = _config.Ui.Topmost;
        }
        else
        {
            Topmost = false;
        }

        Activate();
        Focus();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExiting || _stateMachine.CurrentState == SessionState.Exited || _stateMachine.CurrentState == SessionState.Completed)
        {
            return;
        }

        e.Cancel = true;
        Dispatcher.InvokeAsync(PromptProctorExit);
    }

    private void CloseApplication(string reason)
    {
        if (_isExiting) return;
        _isExiting = true;

        _loggingService.Log("AppExiting", new { reason });
        _touchpadManager?.RestoreGestures();
        _touchpadManager?.Dispose();
        _keyboardHook?.Stop();
        _keyboardHook?.Dispose();
        _focusMonitor?.Dispose();

        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                Application.Current?.Shutdown(0);
            }
            catch { }
            Environment.Exit(0);
        });
    }
}