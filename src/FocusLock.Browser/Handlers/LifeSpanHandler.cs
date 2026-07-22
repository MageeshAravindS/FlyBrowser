using CefSharp;
using FocusLock.Logging;

namespace FocusLock.Browser.Handlers;

public class LifeSpanHandler : ILifeSpanHandler
{
    private readonly LoggingService? _loggingService;

    public LifeSpanHandler(LoggingService? loggingService = null)
    {
        _loggingService = loggingService;
    }

    public bool OnBeforePopup(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, string targetUrl, string targetFrameName, WindowOpenDisposition targetDisposition, bool userGesture, IPopupFeatures popupFeatures, IWindowInfo windowInfo, IBrowserSettings browserSettings, ref bool noJavascriptAccess, out IWebBrowser newBrowser)
    {
        newBrowser = null!;

        _loggingService?.Log("PopupBlocked", new
        {
            targetUrl,
            targetFrameName,
            userGesture
        });

        if (userGesture && !string.IsNullOrWhiteSpace(targetUrl))
        {
            chromiumWebBrowser.Load(targetUrl);
        }

        return true;
    }

    public void OnAfterCreated(IWebBrowser chromiumWebBrowser, IBrowser browser)
    {
    }

    public bool DoClose(IWebBrowser chromiumWebBrowser, IBrowser browser)
    {
        return false;
    }

    public void OnBeforeClose(IWebBrowser chromiumWebBrowser, IBrowser browser)
    {
    }
}
