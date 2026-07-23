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
    private readonly LoginPromptView _loginPromptView;
    private System.Windows.Threading.DispatcherTimer? _loginPollTimer;
    private static readonly System.Net.Http.HttpClient _httpClient = new();

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

        Title = "FlyLock Browser - Student Login";
        Topmost = false;
        WindowStyle = WindowStyle.SingleBorderWindow;
        WindowState = WindowState.Normal;
        Width = 520;
        Height = 680;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        _homeView = new HomeView();
        _homeView.SetAppName(_config.Ui.Branding.AppName);
        _homeView.ExamSubmitted += (s, args) => StartExamFromHome(args.Code, args.StudentEmail);
        _homeView.CodeSubmitted += (s, code) => StartExamFromHome(code, "");
        _homeView.SwitchAccountRequested += OnSwitchAccountRequested;

        _loginPromptView = new LoginPromptView();
        _loginPromptView.OpenBrowserRequested += (s, e) => LaunchDefaultBrowserForLogin();
        _loginPromptView.CheckLoginRequested += async (s, e) => await CheckLoginStatusAsync();

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
            _browserHost = new BrowserHostControl(_config, _loggingService);
            _browserHost.PageLoaded += BrowserHost_PageLoaded;
            _browserHost.PageLoadFailed += BrowserHost_PageLoadFailed;
            _browserHost.AddressChanged += (s, url) => OnBrowserAddressChanged(url);
            _browserHost.EscapeKeyPressed += (s, ev) => Dispatcher.Invoke(PromptProctorExit);

            BrowserContainer.Content = _browserHost;
        }
        catch (Exception ex)
        {
            _loggingService.Log("CefInitError", new { error = ex.ToString() });
        }

        // Check if student session is saved locally
        string? savedEmail = StudentSessionStorage.GetSavedEmail();
        if (!string.IsNullOrEmpty(savedEmail))
        {
            _loggingService.Log("PersistentSessionLoaded", new { email = savedEmail });
            EnterFullscreenLockdownMode(savedEmail);
        }
        else
        {
            ShowLoginPrompt();
        }
    }

    private void StartLockdownHooks()
    {
        if (_keyboardHook != null) return;

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
    }

    private string _authenticatedStudentEmail = string.Empty;

    private void OnBrowserAddressChanged(string url)
    {
        if (string.IsNullOrEmpty(url)) return;

        if (url.Contains("/login-success"))
        {
            string email = "student@bitsathy.ac.in";
            if (url.Contains("email="))
            {
                email = Uri.UnescapeDataString(url.Split("email=")[1].Split('&')[0].Split('#')[0]);
            }
            EnterFullscreenLockdownMode(email);
        }
    }

    private void EnterFullscreenLockdownMode(string email)
    {
        Dispatcher.Invoke(() =>
        {
            _authenticatedStudentEmail = email;
            _homeView.SetStudentEmail(email);

            // Activate low-level security lockdown hooks only after successful login
            StartLockdownHooks();

            // Upgrade window to Fullscreen Lockdown Mode
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            Topmost = true;

            OverlayContainer.Content = _homeView;
            OverlayContainer.Visibility = Visibility.Visible;
            BrowserContainer.Visibility = Visibility.Collapsed;
            BrowserContainer.IsHitTestVisible = false;

            ReassertTopmost();
            _homeView.FocusAccessCodeInput();
        });
    }

    private void ShowLoginPrompt()
    {
        Dispatcher.Invoke(() =>
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = WindowState.Normal;
            Topmost = false;
            Width = 520;
            Height = 680;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            OverlayContainer.Content = _loginPromptView;
            OverlayContainer.Visibility = Visibility.Visible;
            BrowserContainer.Visibility = Visibility.Collapsed;

            LaunchDefaultBrowserForLogin();
            StartLoginPollingTimer();
        });
    }

    private void LaunchDefaultBrowserForLogin()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "http://localhost:8080/student-login.html",
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            _loggingService.Log("LaunchBrowserError", new { error = ex.Message });
        }
    }

    private void StartLoginPollingTimer()
    {
        if (_loginPollTimer != null)
        {
            _loginPollTimer.Stop();
        }

        _loginPollTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.5)
        };
        _loginPollTimer.Tick += async (s, e) =>
        {
            await CheckLoginStatusAsync();
        };
        _loginPollTimer.Start();
    }

    private async System.Threading.Tasks.Task CheckLoginStatusAsync()
    {
        try
        {
            var res = await _httpClient.GetAsync("http://localhost:8080/api/v1/auth/student-me");
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("student", out var studentElem) && studentElem.ValueKind != System.Text.Json.JsonValueKind.Null)
                {
                    if (studentElem.TryGetProperty("email", out var emailElem))
                    {
                        string? email = emailElem.GetString();
                        if (!string.IsNullOrEmpty(email) && email.EndsWith("@bitsathy.ac.in"))
                        {
                            _loginPollTimer?.Stop();
                            StudentSessionStorage.SaveEmail(email);
                            EnterFullscreenLockdownMode(email);
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore network poll errors during startup
        }
    }

    private void OnSwitchAccountRequested(object? sender, EventArgs e)
    {
        StudentSessionStorage.ClearSession();
        _authenticatedStudentEmail = string.Empty;
        ShowLoginPrompt();
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        if (_stateMachine.CurrentState == SessionState.Idle)
        {
            if (!string.IsNullOrEmpty(_authenticatedStudentEmail))
            {
                Topmost = false;
                ReassertTopmost();
                _homeView.FocusAccessCodeInput();
            }
        }
    }

    private void StartExamFromHome(string accessCode, string studentEmail = "")
    {
        accessCode = (accessCode ?? string.Empty).Trim();
        studentEmail = (studentEmail ?? string.Empty).Trim().ToLower();
        _loggingService.Log("ExamCodeSubmitted", new { code = accessCode, email = studentEmail });
        OverlayContainer.Content = _loadingView;
        
        if (_focusMonitor != null)
        {
            _focusMonitor.IsPaused = false;
        }

        _stateMachine.TransitionTo(SessionState.Launching, $"Access code submitted ({accessCode}) for {studentEmail}");

        string baseUrl = "http://localhost:8080";
        if (!string.IsNullOrEmpty(_config.ExamUrl) && Uri.TryCreate(_config.ExamUrl, UriKind.Absolute, out var baseUri))
        {
            baseUrl = $"{baseUri.Scheme}://{baseUri.Authority}";
        }

        string targetUrl = $"{baseUrl}/assessment/{Uri.EscapeDataString(accessCode)}";
        if (!string.IsNullOrEmpty(studentEmail))
        {
            targetUrl += $"?email={Uri.EscapeDataString(studentEmail)}";
        }

        _browserHost?.LoadUrl(targetUrl);
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
            CloseApplication("Proctor exit sequence authenticated");
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