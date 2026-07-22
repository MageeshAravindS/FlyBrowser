using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FocusLock.Focus;

public class LowLevelKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private const uint VK_TAB = 0x09;
    private const uint VK_ESCAPE = 0x1B;
    private const uint VK_F4 = 0x73;
    private const uint VK_LWIN = 0x5B;
    private const uint VK_RWIN = 0x5C;
    private const uint VK_CONTROL = 0x11;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    private readonly HookProc _proc;
    private IntPtr _hookId = IntPtr.Zero;
    private bool _isHookActive;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    public event EventHandler<string>? BlockedKeyCombination;

    public LowLevelKeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_isHookActive) return;
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule curModule = curProcess.MainModule!)
        {
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
            _isHookActive = _hookId != IntPtr.Zero;
        }
    }

    public void Stop()
    {
        if (_isHookActive && _hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            _isHookActive = false;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            KBDLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            uint vk = hookStruct.vkCode;
            bool altDown = (hookStruct.flags & 0x20) != 0;
            bool ctrlDown = (GetKeyState((int)VK_CONTROL) & 0x8000) != 0;
            bool winDown = (GetKeyState((int)VK_LWIN) & 0x8000) != 0 || (GetKeyState((int)VK_RWIN) & 0x8000) != 0;
            bool isInjected = (hookStruct.flags & 0x10) != 0 || (hookStruct.flags & 0x02) != 0;

            // 1. Block Windows Key & Win Shortcuts (Win, Win+Tab, Win+D, Win+Ctrl+Left/Right, Touchpad gestures)
            if (vk == VK_LWIN || vk == VK_RWIN || winDown)
            {
                BlockedKeyCombination?.Invoke(this, "Windows Key / Shortcut / Gesture");
                return (IntPtr)1;
            }

            // 2. Block Alt+Tab, Alt+Esc, Alt+F4
            if (altDown && (vk == VK_TAB || vk == VK_ESCAPE || vk == VK_F4))
            {
                BlockedKeyCombination?.Invoke(this, "Alt Shortcut");
                return (IntPtr)1;
            }

            // 3. Block Ctrl+Esc (Start Menu)
            if (ctrlDown && vk == VK_ESCAPE)
            {
                BlockedKeyCombination?.Invoke(this, "Ctrl+Esc");
                return (IntPtr)1;
            }

            // 4. Block Injected Touchpad Navigation Keys (Virtual Desktop & Task View Swipes)
            if (isInjected && (vk == 0x25 || vk == 0x27 || vk == 0x26 || vk == 0x28 || vk == VK_TAB))
            {
                BlockedKeyCombination?.Invoke(this, "Injected Touchpad Gesture");
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
