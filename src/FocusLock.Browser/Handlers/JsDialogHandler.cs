using CefSharp;
using FocusLock.Logging;

namespace FocusLock.Browser.Handlers;

public class JsDialogHandler : IJsDialogHandler
{
    private readonly LoggingService? _loggingService;

    public JsDialogHandler(LoggingService? loggingService = null)
    {
        _loggingService = loggingService;
    }

    public bool OnJSDialog(IWebBrowser chromiumWebBrowser, IBrowser browser, string originUrl, CefJsDialogType dialogType, string messageText, string defaultPromptText, IJsDialogCallback callback, ref bool suppressMessage)
    {
        _loggingService?.Log("JsDialog", new { originUrl, dialogType = dialogType.ToString(), messageText });
        // Auto-approve JS confirm & alert dialogs
        callback.Continue(true, defaultPromptText);
        return true;
    }

    public bool OnBeforeUnloadDialog(IWebBrowser chromiumWebBrowser, IBrowser browser, string messageText, bool isReload, IJsDialogCallback callback)
    {
        callback.Continue(true, string.Empty);
        return true;
    }

    public void OnResetDialogState(IWebBrowser chromiumWebBrowser, IBrowser browser) { }
    public void OnDialogClosed(IWebBrowser chromiumWebBrowser, IBrowser browser) { }
}
