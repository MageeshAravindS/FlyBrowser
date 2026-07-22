using FocusLock.Security;
using Xunit;

namespace FocusLock.Security.Tests;

public class ExitAuthorizationServiceTests
{
    [Fact]
    public void VerifyPassword_CorrectPassword_ReturnsTrue()
    {
        string hash = ExitAuthorizationService.HashPassword("MasterPass123!");
        var authService = new ExitAuthorizationService(hash, "Ctrl+Alt+Shift+Q");

        Assert.True(authService.VerifyPassword("MasterPass123!"));
        Assert.False(authService.VerifyPassword("WrongPassword"));
    }

    [Fact]
    public void IsExitShortcut_MatchingCombination_ReturnsTrue()
    {
        var authService = new ExitAuthorizationService(null, "Ctrl+Alt+Shift+Q");

        Assert.True(authService.IsExitShortcut(ctrlPressed: true, altPressed: true, shiftPressed: true, keyName: "Q"));
        Assert.False(authService.IsExitShortcut(ctrlPressed: true, altPressed: false, shiftPressed: true, keyName: "Q"));
        Assert.False(authService.IsExitShortcut(ctrlPressed: true, altPressed: true, shiftPressed: true, keyName: "A"));
    }
}
