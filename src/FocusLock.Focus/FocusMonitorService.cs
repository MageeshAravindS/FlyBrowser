using System;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using FocusLock.Core;

namespace FocusLock.Focus;

public class FocusMonitorService : IDisposable
{
    private readonly SessionStateMachine _stateMachine;
    private readonly Window _window;
    private readonly int _debounceMs;
    private Timer? _debounceTimer;
    private bool _isCurrentlyFocused = true;
    private readonly object _lock = new();

    public bool IsPaused { get; set; } = false;

    public FocusMonitorService(Window window, SessionStateMachine stateMachine, int debounceMs = 250)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _debounceMs = Math.Max(0, debounceMs);

        _window.Activated += OnWindowActivated;
        _window.Deactivated += OnWindowDeactivated;

        _window.Loaded += (s, e) =>
        {
            var hwnd = new WindowInteropHelper(_window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                HwndSource source = HwndSource.FromHwnd(hwnd);
                source?.AddHook(WndProcHook);
            }
        };
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        HandleRefocus();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        HandleFocusLoss();
    }

    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32FocusInterop.WM_ACTIVATE)
        {
            int activateState = (int)(wParam.ToInt64() & 0xFFFF);
            if (activateState == Win32FocusInterop.WA_INACTIVE)
            {
                HandleFocusLoss();
            }
            else
            {
                HandleRefocus();
            }
        }
        return IntPtr.Zero;
    }

    public void HandleFocusLoss()
    {
        if (IsPaused) return;

        lock (_lock)
        {
            if (!_isCurrentlyFocused) return;
            _isCurrentlyFocused = false;

            _debounceTimer?.Dispose();

            if (_debounceMs <= 0)
            {
                TriggerFocusLoss();
            }
            else
            {
                _debounceTimer = new Timer(_ =>
                {
                    lock (_lock)
                    {
                        if (!_isCurrentlyFocused && !IsPaused)
                        {
                            TriggerFocusLoss();
                        }
                    }
                }, null, _debounceMs, Timeout.Infinite);
            }
        }
    }

    public void HandleRefocus()
    {
        lock (_lock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;

            bool wasLost = !_isCurrentlyFocused;
            _isCurrentlyFocused = true;

            if (wasLost && !IsPaused)
            {
                _stateMachine.RegisterFocusRestored();
            }
        }
    }

    private void TriggerFocusLoss()
    {
        string lostTitle = Win32FocusInterop.GetForegroundWindowTitle();
        _stateMachine.RegisterFocusLoss(lostTitle);
    }

    public void Dispose()
    {
        _window.Activated -= OnWindowActivated;
        _window.Deactivated -= OnWindowDeactivated;
        _debounceTimer?.Dispose();
        GC.SuppressFinalize(this);
    }
}
