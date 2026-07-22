using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FocusLock.Config;

public class ConfigService
{
    public FocusLockConfig Config { get; private set; } = new();
    public string ConfigPathUsed { get; private set; } = string.Empty;

    public FocusLockConfig Load(string[] args)
    {
        string? specifiedPath = ParseConfigPathArg(args);
        string path = ResolveConfigPath(specifiedPath);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"FocusLock configuration file not found at '{path}'. Please ensure a valid config.json is present or pass --config <path>.");
        }

        ConfigPathUsed = path;
        string json = File.ReadAllText(path);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        FocusLockConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<FocusLockConfig>(json, options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Configuration file at '{path}' contains invalid JSON formatting: {ex.Message}", ex);
        }

        if (config == null)
        {
            throw new InvalidDataException($"Configuration file at '{path}' evaluated to null.");
        }

        ValidateConfig(config);
        Config = config;
        return config;
    }

    public static void ValidateConfig(FocusLockConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ExamUrl))
        {
            throw new InvalidDataException("Configuration requirement failed: 'examUrl' must be specified and cannot be empty.");
        }

        if (!Uri.TryCreate(config.ExamUrl, UriKind.Absolute, out var uriResult) ||
            (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidDataException($"Configuration requirement failed: 'examUrl' must be a valid absolute HTTP or HTTPS URL. Given: '{config.ExamUrl}'");
        }

        if (config.FocusMonitoring.WarningThreshold < 1)
        {
            throw new InvalidDataException("Configuration requirement failed: 'focusMonitoring.warningThreshold' must be >= 1.");
        }

        if (config.FocusMonitoring.TerminationThreshold < config.FocusMonitoring.WarningThreshold)
        {
            throw new InvalidDataException("Configuration requirement failed: 'focusMonitoring.terminationThreshold' must be greater than or equal to 'warningThreshold'.");
        }

        if (config.FocusMonitoring.FocusLossDebounceMs < 0)
        {
            config.FocusMonitoring.FocusLossDebounceMs = 250;
        }

        if (!string.IsNullOrEmpty(uriResult.Host) && !config.AllowedDomains.Contains(uriResult.Host, StringComparer.OrdinalIgnoreCase))
        {
            config.AllowedDomains.Add(uriResult.Host);
        }
    }

    private static string? ParseConfigPathArg(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i].Equals("--config", StringComparison.OrdinalIgnoreCase) || args[i].Equals("-c", StringComparison.OrdinalIgnoreCase))
                && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static string ResolveConfigPath(string? specifiedPath)
    {
        if (!string.IsNullOrWhiteSpace(specifiedPath))
        {
            if (File.Exists(specifiedPath))
            {
                return Path.GetFullPath(specifiedPath);
            }

            string baseDirCandidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, specifiedPath);
            if (File.Exists(baseDirCandidate))
            {
                return baseDirCandidate;
            }

            return Path.GetFullPath(specifiedPath);
        }

        string localConfig = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        if (File.Exists(localConfig))
        {
            return localConfig;
        }

        string rootDirConfig = Path.Combine(Directory.GetCurrentDirectory(), "config.json");
        if (File.Exists(rootDirConfig))
        {
            return rootDirConfig;
        }

        string programDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FocusLock", "config.json");
        return programDataPath;
    }
}
