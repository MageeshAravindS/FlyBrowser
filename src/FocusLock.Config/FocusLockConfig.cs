using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FocusLock.Config;

public class FocusLockConfig
{
    [JsonPropertyName("examUrl")]
    public string ExamUrl { get; set; } = string.Empty;

    [JsonPropertyName("allowedDomains")]
    public List<string> AllowedDomains { get; set; } = new();

    [JsonPropertyName("focusMonitoring")]
    public FocusMonitoringConfig FocusMonitoring { get; set; } = new();

    [JsonPropertyName("ui")]
    public UiConfig Ui { get; set; } = new();

    [JsonPropertyName("logging")]
    public LoggingConfig Logging { get; set; } = new();

    [JsonPropertyName("exitAuthorization")]
    public ExitAuthorizationConfig ExitAuthorization { get; set; } = new();

    [JsonPropertyName("network")]
    public NetworkConfig Network { get; set; } = new();
}

public class FocusMonitoringConfig
{
    [JsonPropertyName("warningThreshold")]
    public int WarningThreshold { get; set; } = 1;

    [JsonPropertyName("terminationThreshold")]
    public int TerminationThreshold { get; set; } = 3;

    [JsonPropertyName("focusLossDebounceMs")]
    public int FocusLossDebounceMs { get; set; } = 250;
}

public class UiConfig
{
    [JsonPropertyName("branding")]
    public BrandingConfig Branding { get; set; } = new();

    [JsonPropertyName("topmost")]
    public bool Topmost { get; set; } = true;
}

public class BrandingConfig
{
    [JsonPropertyName("appName")]
    public string AppName { get; set; } = "FlyLock Browser";

    [JsonPropertyName("logoPath")]
    public string? LogoPath { get; set; }

    [JsonPropertyName("accentColor")]
    public string AccentColor { get; set; } = "#2D6CDF";
}

public class LoggingConfig
{
    [JsonPropertyName("logDirectory")]
    public string? LogDirectory { get; set; }

    [JsonPropertyName("hashChain")]
    public bool HashChain { get; set; } = true;
}

public class ExitAuthorizationConfig
{
    [JsonPropertyName("passwordHash")]
    public string? PasswordHash { get; set; }

    [JsonPropertyName("keySequence")]
    public string KeySequence { get; set; } = "Ctrl+Alt+Shift+Q";
}

public class NetworkConfig
{
    [JsonPropertyName("connectTimeoutMs")]
    public int ConnectTimeoutMs { get; set; } = 10000;

    [JsonPropertyName("retryAttempts")]
    public int RetryAttempts { get; set; } = 3;
}
