using System;
using CefSharp;
using FocusLock.Logging;

namespace FocusLock.Browser.Handlers;

public class DownloadHandler : IDownloadHandler
{
    private readonly LoggingService? _loggingService;

    public DownloadHandler(LoggingService? loggingService = null)
    {
        _loggingService = loggingService;
    }

    public bool CanDownload(IWebBrowser chromiumWebBrowser, IBrowser browser, string url, string requestMethod)
    {
        return false;
    }

    public bool OnBeforeDownload(IWebBrowser chromiumWebBrowser, IBrowser browser, DownloadItem downloadItem, IBeforeDownloadCallback callback)
    {
        _loggingService?.Log("DownloadBlocked", new
        {
            url = downloadItem.Url,
            suggestedFileName = downloadItem.SuggestedFileName,
            totalBytes = downloadItem.TotalBytes
        });

        using (callback)
        {
            callback.Continue(string.Empty, showDialog: false);
        }
        return true;
    }

    public void OnDownloadUpdated(IWebBrowser chromiumWebBrowser, IBrowser browser, DownloadItem downloadItem, IDownloadItemCallback callback)
    {
        if (downloadItem.IsInProgress)
        {
            callback.Cancel();
        }
    }
}
