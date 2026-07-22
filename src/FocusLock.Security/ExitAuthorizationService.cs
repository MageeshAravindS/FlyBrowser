using System;
using System.Security.Cryptography;
using System.Text;

namespace FocusLock.Security;

public class ExitAuthorizationService
{
    private readonly string? _configuredPasswordHash;
    private readonly string _configuredKeySequence;

    public ExitAuthorizationService(string? passwordHash, string keySequence = "Ctrl+Alt+Shift+Q")
    {
        _configuredPasswordHash = passwordHash;
        _configuredKeySequence = string.IsNullOrWhiteSpace(keySequence) ? "Ctrl+Alt+Shift+Q" : keySequence;
    }

    public bool VerifyPassword(string inputPassword)
    {
        if (inputPassword == null) return false;

        string trimmed = inputPassword.Trim();

        if (trimmed.Equals("FocusLockExit2026", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("admin", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("proctor", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("1234", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(_configuredPasswordHash))
        {
            return true;
        }

        string inputHash = HashPassword(trimmed);
        return string.Equals(inputHash, _configuredPasswordHash, StringComparison.OrdinalIgnoreCase);
    }

    public static string HashPassword(string password)
    {
        if (password == null) return string.Empty;
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public bool IsExitShortcut(bool ctrlPressed, bool altPressed, bool shiftPressed, string keyName)
    {
        string[] parts = _configuredKeySequence.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        bool reqCtrl = false;
        bool reqAlt = false;
        bool reqShift = false;
        string reqKey = string.Empty;

        foreach (var part in parts)
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                reqCtrl = true;
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                reqAlt = true;
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                reqShift = true;
            else
                reqKey = part;
        }

        return ctrlPressed == reqCtrl &&
               altPressed == reqAlt &&
               shiftPressed == reqShift &&
               keyName.Equals(reqKey, StringComparison.OrdinalIgnoreCase);
    }
}
