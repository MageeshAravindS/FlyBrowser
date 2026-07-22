using System;
using System.Runtime.InteropServices;
using System.Text;

namespace FocusLock.Focus;

public static class Win32FocusInterop
{
    public const int WM_ACTIVATE = 0x0006;
    public const int WA_INACTIVE = 0;
    public const int SW_MAXIMIZE = 3;
    public const int SW_RESTORE = 9;
    public const int SW_SHOW = 5;

    public const int WM_GESTURE = 0x0119;
    public const int WM_GESTURENOTIFY = 0x011A;

    [StructLayout(LayoutKind.Sequential)]
    public struct GESTURECONFIG
    {
        public uint dwID;
        public uint dwWant;
        public uint dwBlock;
    }

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetGestureConfig(
        IntPtr hwnd,
        uint dwReserved,
        uint cIDs,
        GESTURECONFIG[] pGestureConfig,
        uint cbSize);

    public static void ForceForegroundWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;

        IntPtr foreHwnd = GetForegroundWindow();
        if (foreHwnd == hWnd)
        {
            ShowWindow(hWnd, SW_RESTORE);
            ShowWindow(hWnd, SW_MAXIMIZE);
            return;
        }

        uint foreThread = GetWindowThreadProcessId(foreHwnd, out _);
        uint currentThread = GetCurrentThreadId();

        try
        {
            if (foreThread != 0 && foreThread != currentThread)
            {
                AttachThreadInput(currentThread, foreThread, true);
                ShowWindow(hWnd, SW_RESTORE);
                ShowWindow(hWnd, SW_MAXIMIZE);
                SetForegroundWindow(hWnd);
                AttachThreadInput(currentThread, foreThread, false);
            }
            else
            {
                ShowWindow(hWnd, SW_RESTORE);
                ShowWindow(hWnd, SW_MAXIMIZE);
                SetForegroundWindow(hWnd);
            }
        }
        catch
        {
            ShowWindow(hWnd, SW_RESTORE);
            ShowWindow(hWnd, SW_MAXIMIZE);
            SetForegroundWindow(hWnd);
        }
    }

    public static string GetForegroundWindowTitle()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return "Unknown (No Active Window)";

        int length = GetWindowTextLength(hwnd);
        if (length == 0)
            return "Unknown (Untitled Window)";

        StringBuilder sb = new StringBuilder(length + 1);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static void DisableWindowGestures(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            var config = new GESTURECONFIG
            {
                dwID = 0, // GC_ALLGESTURES
                dwWant = 0,
                dwBlock = 0xFFFFFFFF // Block all gestures
            };

            SetGestureConfig(hwnd, 0, 1, new[] { config }, (uint)Marshal.SizeOf<GESTURECONFIG>());
        }
        catch { }
    }
}
