using System;
using System.IO;
using System.Threading.Tasks;
using FocusLock.Logging;
using Xunit;

namespace FocusLock.Logging.Tests;

public class LoggingServiceTests
{
    [Fact]
    public async Task Logging_WritesStructuredJsonlAndMaintainsHashChain()
    {
        string testDir = Path.Combine(Path.GetTempPath(), "FocusLockLogTest_" + Guid.NewGuid().ToString("N"));
        string sessionId = "test-session-123";

        await using (var logger = new LoggingService(sessionId, testDir, enableHashChain: true))
        {
            logger.Log("SessionStarted", new { mode = "test" });
            logger.Log("FocusLost", new { count = 1 });
            logger.Log("SessionTerminated", new { reason = "Threshold reached" });
        }

        string logFile = Directory.GetFiles(testDir, "*.jsonl")[0];
        Assert.True(File.Exists(logFile));

        bool isValid = LoggingService.VerifyLogIntegrity(logFile, out string errorReason);
        Assert.True(isValid, $"Log integrity check failed: {errorReason}");

        Directory.Delete(testDir, true);
    }

    [Fact]
    public async Task Logging_DetectsTamperedLogFile()
    {
        string testDir = Path.Combine(Path.GetTempPath(), "FocusLockTamperTest_" + Guid.NewGuid().ToString("N"));
        string sessionId = "tamper-session-456";

        await using (var logger = new LoggingService(sessionId, testDir, enableHashChain: true))
        {
            logger.Log("Event1", new { data = "A" });
            logger.Log("Event2", new { data = "B" });
        }

        string logFile = Directory.GetFiles(testDir, "*.jsonl")[0];
        string[] lines = File.ReadAllLines(logFile);

        lines[1] = lines[1].Replace("\"data\":\"B\"", "\"data\":\"TAMPERED\"");
        File.WriteAllLines(logFile, lines);

        bool isValid = LoggingService.VerifyLogIntegrity(logFile, out string errorReason);
        Assert.False(isValid);
        Assert.Contains("Hash mismatch", errorReason);

        Directory.Delete(testDir, true);
    }
}
