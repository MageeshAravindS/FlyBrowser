using System;
using System.IO;
using System.Text.Json;

namespace FocusLock.Config;

public static class StudentSessionStorage
{
    private static readonly string FolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlyLock");
    private static readonly string FilePath = Path.Combine(FolderPath, "student_session.json");

    public static string? GetSavedEmail()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var json = File.ReadAllText(FilePath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("email", out var elem))
            {
                var email = elem.GetString()?.Trim().ToLower();
                if (!string.IsNullOrEmpty(email) && email.EndsWith("@bitsathy.ac.in"))
                {
                    return email;
                }
            }
        }
        catch
        {
            // Fallback on corrupt read
        }
        return null;
    }

    public static void SaveEmail(string email)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(email)) return;
            email = email.Trim().ToLower();
            Directory.CreateDirectory(FolderPath);
            var payload = JsonSerializer.Serialize(new { email = email, savedAt = DateTime.UtcNow });
            File.WriteAllText(FilePath, payload);
        }
        catch
        {
            // Ignore write errors
        }
    }

    public static void ClearSession()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
        catch
        {
            // Ignore delete errors
        }
    }
}
