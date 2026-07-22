using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace FocusLock.Focus;

public class TouchpadManager : IDisposable
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad";
    private const string WinlogonPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";

    private const uint HWND_BROADCAST = 0xFFFF;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint Msg,
        UIntPtr wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out UIntPtr lpdwResult);

    private object? _originalThreeFingerGroup;
    private object? _originalFourFingerGroup;
    private object? _originalThreeFingerTap;
    private object? _originalFourFingerTap;
    private object? _originalAutoRestartShell;

    private bool _isApplied;
    private bool _explorerTerminated;
    private CancellationTokenSource? _watchdogCts;
    private readonly EventHandler _processExitHandler;

    public TouchpadManager()
    {
        _processExitHandler = (s, e) => RestoreGestures();
        AppDomain.CurrentDomain.ProcessExit += _processExitHandler;
    }

    public void DisableGestures()
    {
        if (!_isApplied)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
                if (key != null)
                {
                    _originalThreeFingerGroup = key.GetValue("ThreeFingerGroup");
                    _originalFourFingerGroup = key.GetValue("FourFingerGroup");
                    _originalThreeFingerTap = key.GetValue("ThreeFingerTapEnabled");
                    _originalFourFingerTap = key.GetValue("FourFingerTapEnabled");

                    key.SetValue("ThreeFingerGroup", 0, RegistryValueKind.DWord);
                    key.SetValue("FourFingerGroup", 0, RegistryValueKind.DWord);
                    key.SetValue("ThreeFingerTapEnabled", 0, RegistryValueKind.DWord);
                    key.SetValue("FourFingerTapEnabled", 0, RegistryValueKind.DWord);

                    _isApplied = true;
                    NotifySystemSettingsChanged();
                }
            }
            catch { }
        }

        // Disable Windows AutoRestartShell so Winlogon doesn't automatically relaunch explorer.exe
        DisableAutoRestartShell();

        // Stop explorer.exe and start watchdog thread to keep it closed during session
        StopExplorer();
        StartExplorerWatchdog();
    }

    public void RestoreGestures()
    {
        // Stop watchdog thread first
        StopExplorerWatchdog();

        if (_isApplied)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
                if (key != null)
                {
                    if (_originalThreeFingerGroup != null) key.SetValue("ThreeFingerGroup", _originalThreeFingerGroup);
                    else key.DeleteValue("ThreeFingerGroup", false);

                    if (_originalFourFingerGroup != null) key.SetValue("FourFingerGroup", _originalFourFingerGroup);
                    else key.DeleteValue("FourFingerGroup", false);

                    if (_originalThreeFingerTap != null) key.SetValue("ThreeFingerTapEnabled", _originalThreeFingerTap);
                    else key.DeleteValue("ThreeFingerTapEnabled", false);

                    if (_originalFourFingerTap != null) key.SetValue("FourFingerTapEnabled", _originalFourFingerTap);
                    else key.DeleteValue("FourFingerTapEnabled", false);

                    _isApplied = false;
                    NotifySystemSettingsChanged();
                }
            }
            catch { }
        }

        // Restore Winlogon AutoRestartShell setting
        RestoreAutoRestartShell();

        // Relaunch explorer.exe
        StartExplorer();
    }

    private void DisableAutoRestartShell()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(WinlogonPath, true);
            if (key != null)
            {
                _originalAutoRestartShell = key.GetValue("AutoRestartShell");
                key.SetValue("AutoRestartShell", 0, RegistryValueKind.DWord);
            }
        }
        catch { }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(WinlogonPath, true);
            if (key != null)
            {
                key.SetValue("AutoRestartShell", 0, RegistryValueKind.DWord);
            }
        }
        catch { }
    }

    private void RestoreAutoRestartShell()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(WinlogonPath, true);
            if (key != null)
            {
                if (_originalAutoRestartShell != null)
                {
                    key.SetValue("AutoRestartShell", _originalAutoRestartShell);
                }
                else
                {
                    key.SetValue("AutoRestartShell", 1, RegistryValueKind.DWord);
                }
            }
        }
        catch { }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(WinlogonPath, true);
            if (key != null)
            {
                key.SetValue("AutoRestartShell", 1, RegistryValueKind.DWord);
            }
        }
        catch { }
    }

    private void StopExplorer()
    {
        try
        {
            var explorerProcesses = Process.GetProcessesByName("explorer");
            if (explorerProcesses.Length > 0)
            {
                foreach (var proc in explorerProcesses)
                {
                    try
                    {
                        proc.Kill();
                        proc.WaitForExit(500);
                    }
                    catch { }
                }
                _explorerTerminated = true;
            }
        }
        catch { }
    }

    private void StartExplorerWatchdog()
    {
        if (_watchdogCts != null) return;
        _watchdogCts = new CancellationTokenSource();
        var token = _watchdogCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var procs = Process.GetProcessesByName("explorer");
                    if (procs.Length > 0)
                    {
                        foreach (var p in procs)
                        {
                            try { p.Kill(); } catch { }
                        }
                        _explorerTerminated = true;
                    }
                }
                catch { }

                try
                {
                    await Task.Delay(500, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    private void StopExplorerWatchdog()
    {
        if (_watchdogCts != null)
        {
            try
            {
                _watchdogCts.Cancel();
                _watchdogCts.Dispose();
            }
            catch { }
            _watchdogCts = null;
        }
    }

    private void StartExplorer()
    {
        if (!_explorerTerminated) return;

        try
        {
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var explorerPath = Path.Combine(winDir, "explorer.exe");
            if (File.Exists(explorerPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = explorerPath,
                    UseShellExecute = true
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = true
                });
            }
            _explorerTerminated = false;
        }
        catch { }
    }

    private static void NotifySystemSettingsChanged()
    {
        try
        {
            SendMessageTimeout((IntPtr)HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, RegistryPath, SMTO_ABORTIFHUNG, 500, out _);
            SendMessageTimeout((IntPtr)HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, "Environment", SMTO_ABORTIFHUNG, 500, out _);
        }
        catch { }
    }

    public void Dispose()
    {
        AppDomain.CurrentDomain.ProcessExit -= _processExitHandler;
        RestoreGestures();
        GC.SuppressFinalize(this);
    }
}
