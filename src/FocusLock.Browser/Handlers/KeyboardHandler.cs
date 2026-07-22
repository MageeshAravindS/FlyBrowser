using System;
using CefSharp;

namespace FocusLock.Browser.Handlers;

public class KeyboardHandler : IKeyboardHandler
{
    public event EventHandler? EscapeKeyPressed;

    public bool OnPreKeyEvent(IWebBrowser chromiumWebBrowser, IBrowser browser, KeyType type, int windowsKeyCode, int nativeKeyCode, CefEventFlags modifiers, bool isSystemKey, ref bool isKeyboardShortcut)
    {
        bool isCtrl = modifiers.HasFlag(CefEventFlags.ControlDown);
        bool isShift = modifiers.HasFlag(CefEventFlags.ShiftDown);
        bool isAlt = modifiers.HasFlag(CefEventFlags.AltDown);

        // Escape Key (27): Prompt Proctor Exit
        if (windowsKeyCode == 27 && type == KeyType.RawKeyDown)
        {
            EscapeKeyPressed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        // DevTools: F12 (123), Ctrl+Shift+I (73), Ctrl+Shift+J (74), Ctrl+Shift+C (67)
        if (windowsKeyCode == 123) return true;
        if (isCtrl && isShift && (windowsKeyCode == 73 || windowsKeyCode == 74 || windowsKeyCode == 67)) return true;

        // View Source: Ctrl+U (85)
        if (isCtrl && windowsKeyCode == 85) return true;

        // New Window/Tab/Close: Ctrl+N (78), Ctrl+T (84), Ctrl+W (87)
        if (isCtrl && (windowsKeyCode == 78 || windowsKeyCode == 84 || windowsKeyCode == 87)) return true;

        // Tab Switching: Ctrl+Tab (9), Ctrl+1..9 (49..57)
        if (isCtrl && windowsKeyCode == 9) return true;
        if (isCtrl && windowsKeyCode >= 49 && windowsKeyCode <= 57) return true;

        // Print: Ctrl+P (80), Save: Ctrl+S (83)
        if (isCtrl && (windowsKeyCode == 80 || windowsKeyCode == 83)) return true;

        // Navigation Back/Forward: Alt+Left (37), Alt+Right (39), VK_BROWSER_BACK (166), VK_BROWSER_FORWARD (167)
        if (isAlt && (windowsKeyCode == 37 || windowsKeyCode == 39)) return true;
        if (windowsKeyCode == 166 || windowsKeyCode == 167) return true;

        // Fullscreen Toggle: F11 (122)
        if (windowsKeyCode == 122) return true;

        // Alt+F4 (115)
        if (isAlt && windowsKeyCode == 115) return true;

        return false;
    }

    public bool OnKeyEvent(IWebBrowser chromiumWebBrowser, IBrowser browser, KeyType type, int windowsKeyCode, int nativeKeyCode, CefEventFlags modifiers, bool isSystemKey)
    {
        return false;
    }
}
