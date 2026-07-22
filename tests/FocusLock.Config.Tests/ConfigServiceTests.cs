using System.IO;
using FocusLock.Config;
using Xunit;

namespace FocusLock.Config.Tests;

public class ConfigServiceTests
{
    [Fact]
    public void ValidateConfig_ValidExamUrl_Passes()
    {
        var config = new FocusLockConfig
        {
            ExamUrl = "https://exam.university.edu/test/123",
            FocusMonitoring = new FocusMonitoringConfig
            {
                WarningThreshold = 2,
                TerminationThreshold = 3
            }
        };

        ConfigService.ValidateConfig(config);
        Assert.Contains("exam.university.edu", config.AllowedDomains);
    }

    [Fact]
    public void ValidateConfig_InvalidUrl_ThrowsInvalidDataException()
    {
        var config = new FocusLockConfig
        {
            ExamUrl = "invalid-url-string"
        };

        Assert.Throws<InvalidDataException>(() => ConfigService.ValidateConfig(config));
    }

    [Fact]
    public void ValidateConfig_TerminationLessThanWarning_ThrowsInvalidDataException()
    {
        var config = new FocusLockConfig
        {
            ExamUrl = "https://example.com",
            FocusMonitoring = new FocusMonitoringConfig
            {
                WarningThreshold = 3,
                TerminationThreshold = 2
            }
        };

        Assert.Throws<InvalidDataException>(() => ConfigService.ValidateConfig(config));
    }
}
