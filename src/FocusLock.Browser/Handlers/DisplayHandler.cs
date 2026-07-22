using CefSharp;
using System.Collections.Generic;
using CefSharp.Structs;

namespace FocusLock.Browser.Handlers;

public class DisplayHandler : IDisplayHandler
{
    public void OnAddressChanged(IWebBrowser chromiumWebBrowser, AddressChangedEventArgs addressChangedArgs)
    {
    }

    public void OnTitleChanged(IWebBrowser chromiumWebBrowser, TitleChangedEventArgs titleChangedArgs)
    {
    }

    public void OnFaviconUrlChange(IWebBrowser chromiumWebBrowser, IBrowser browser, IList<string> urls)
    {
    }

    public void OnFullscreenModeChange(IWebBrowser chromiumWebBrowser, IBrowser browser, bool fullscreen)
    {
    }

    public bool OnCursorChange(IWebBrowser chromiumWebBrowser, IBrowser browser, System.IntPtr cursor, CefSharp.Enums.CursorType type, CursorInfo customCursorInfo)
    {
        return false;
    }

    public void OnStatusMessage(IWebBrowser chromiumWebBrowser, StatusMessageEventArgs statusMessageArgs)
    {
    }

    public bool OnConsoleMessage(IWebBrowser chromiumWebBrowser, ConsoleMessageEventArgs consoleMessageArgs)
    {
        return true;
    }

    public bool OnAutoResize(IWebBrowser chromiumWebBrowser, IBrowser browser, Size newSize)
    {
        return false;
    }

    public void OnLoadingProgressChange(IWebBrowser chromiumWebBrowser, IBrowser browser, double progress)
    {
    }

    public bool OnTooltipChanged(IWebBrowser chromiumWebBrowser, ref string text)
    {
        text = string.Empty;
        return true;
    }
}
