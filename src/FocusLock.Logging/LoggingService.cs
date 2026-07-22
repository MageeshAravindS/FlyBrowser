using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace FocusLock.Logging;

public class LoggingService : IDisposable, IAsyncDisposable
{
    private readonly string _logFilePath;
    private readonly bool _enableHashChain;
    private readonly string _sessionId;
    private readonly Channel<LogEvent> _logChannel;
    private readonly Task _processingTask;
    private readonly object _hashLock = new();

    public string CurrentHash { get; private set; } = "GENESIS_HASH_00000000000000000000000000000000000000000000000000000000";
    public string LogFilePath => _logFilePath;

    public LoggingService(string sessionId, string? customDirectory = null, bool enableHashChain = true)
    {
        _sessionId = sessionId;
        _enableHashChain = enableHashChain;

        string directory = !string.IsNullOrWhiteSpace(customDirectory)
            ? customDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FocusLock", "logs");

        Directory.CreateDirectory(directory);
        string filename = $"session-{_sessionId}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.jsonl";
        _logFilePath = Path.Combine(directory, filename);

        _logChannel = Channel.CreateUnbounded<LogEvent>(new UnboundedChannelOptions
        {
            SingleReader = true
        });

        _processingTask = Task.Run(ProcessLogQueueAsync);
    }

    public void Log(string eventType, object? payload = null)
    {
        string prevHash;
        string newHash;

        lock (_hashLock)
        {
            prevHash = CurrentHash;
            string timestamp = DateTime.UtcNow.ToString("o");
            string payloadString = payload != null ? JsonSerializer.Serialize(payload) : "{}";
            string rawContent = $"{timestamp}|{_sessionId}|{eventType}|{payloadString}|{prevHash}";

            newHash = _enableHashChain ? ComputeSha256(rawContent) : "HASH_CHAIN_DISABLED";
            CurrentHash = newHash;

            var logEvent = new LogEvent
            {
                Timestamp = timestamp,
                SessionId = _sessionId,
                EventType = eventType,
                Payload = payload,
                PreviousHash = prevHash,
                Hash = newHash
            };

            _logChannel.Writer.TryWrite(logEvent);
        }
    }

    private async Task ProcessLogQueueAsync()
    {
        using var stream = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, Encoding.UTF8);

        await foreach (var logEvent in _logChannel.Reader.ReadAllAsync())
        {
            string line = JsonSerializer.Serialize(logEvent);
            await writer.WriteLineAsync(line);
            await writer.FlushAsync();
        }
    }

    public static bool VerifyLogIntegrity(string filePath, out string errorReason)
    {
        errorReason = string.Empty;
        if (!File.Exists(filePath))
        {
            errorReason = "Log file does not exist.";
            return false;
        }

        string expectedPrevHash = "GENESIS_HASH_00000000000000000000000000000000000000000000000000000000";
        int lineNumber = 0;

        foreach (var line in File.ReadLines(filePath))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            LogEvent? evt;
            try
            {
                evt = JsonSerializer.Deserialize<LogEvent>(line);
            }
            catch (Exception ex)
            {
                errorReason = $"Line {lineNumber}: JSON deserialization failed: {ex.Message}";
                return false;
            }

            if (evt == null)
            {
                errorReason = $"Line {lineNumber}: Event evaluated to null.";
                return false;
            }

            if (evt.PreviousHash != expectedPrevHash)
            {
                errorReason = $"Line {lineNumber}: Broken hash chain! Expected previousHash '{expectedPrevHash}', but found '{evt.PreviousHash}'.";
                return false;
            }

            if (evt.Hash != "HASH_CHAIN_DISABLED")
            {
                string payloadString = evt.Payload != null ? JsonSerializer.Serialize(evt.Payload) : "{}";
                string rawContent = $"{evt.Timestamp}|{evt.SessionId}|{evt.EventType}|{payloadString}|{evt.PreviousHash}";
                string calculatedHash = ComputeSha256(rawContent);

                if (calculatedHash != evt.Hash)
                {
                    errorReason = $"Line {lineNumber}: Hash mismatch! Computed '{calculatedHash}', but log contains '{evt.Hash}'. Content was tampered with.";
                    return false;
                }
            }

            expectedPrevHash = evt.Hash;
        }

        return true;
    }

    private static string ComputeSha256(string raw)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public void Dispose()
    {
        _logChannel.Writer.Complete();
        _processingTask.GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        _logChannel.Writer.Complete();
        await _processingTask;
        GC.SuppressFinalize(this);
    }
}
