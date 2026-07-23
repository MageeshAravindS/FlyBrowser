using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using CefSharp;
using CefSharp.Wpf;
using FocusLock.Browser.Handlers;
using FocusLock.Config;
using FocusLock.Logging;

namespace FocusLock.Browser;

public class BrowserHostControl : UserControl
{
    private ChromiumWebBrowser? _browser;
    private LoggingService? _loggingService;
    private FocusLockConfig? _config;

    public event EventHandler? PageLoaded;
    public event EventHandler<string>? PageLoadFailed;
    public event EventHandler<string>? AddressChanged;
    public event EventHandler? EscapeKeyPressed;

    public ChromiumWebBrowser? ChromiumBrowser => _browser;

    public BrowserHostControl()
    {
    }

    public BrowserHostControl(FocusLockConfig config, LoggingService? loggingService = null)
    {
        _config = config;
        _loggingService = loggingService;
        InitializeBrowserWithConfig(config, loggingService);
    }

    public void Initialize(FocusLockConfig config, LoggingService? loggingService = null)
    {
        if (_browser != null) return;
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _loggingService = loggingService;
        InitializeBrowserWithConfig(_config, loggingService);
    }

    private void InitializeBrowserWithConfig(FocusLockConfig config, LoggingService? loggingService)
    {
        _browser = new ChromiumWebBrowser
        {
            Address = config.ExamUrl,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var keyboardHandler = new KeyboardHandler();
        keyboardHandler.EscapeKeyPressed += (s, e) => Dispatcher.Invoke(() => EscapeKeyPressed?.Invoke(this, EventArgs.Empty));

        _browser.MenuHandler = new ContextMenuHandler();
        _browser.KeyboardHandler = keyboardHandler;
        _browser.DownloadHandler = new DownloadHandler(loggingService);
        _browser.LifeSpanHandler = new LifeSpanHandler(loggingService);
        _browser.RequestHandler = new RequestHandler(config.AllowedDomains, loggingService);
        _browser.DisplayHandler = new DisplayHandler();
        _browser.JsDialogHandler = new JsDialogHandler(loggingService);

        _browser.AddressChanged += (s, e) =>
        {
            Dispatcher.Invoke(() =>
            {
                string newUrl = e.NewValue?.ToString() ?? _browser?.Address ?? string.Empty;
                if (!string.IsNullOrEmpty(newUrl))
                {
                    AddressChanged?.Invoke(this, newUrl);
                }
            });
        };

        _browser.LoadingStateChanged += (s, e) =>
        {
            if (!e.IsLoading)
            {
                Dispatcher.Invoke(() =>
                {
                    string currentUrl = _browser?.Address ?? config.ExamUrl;
                    loggingService?.Log("NavigationCompleted", new { url = currentUrl });
                    PageLoaded?.Invoke(this, EventArgs.Empty);
                });
            }
        };

        _browser.LoadError += (s, e) =>
        {
            if (e.ErrorCode == CefErrorCode.Aborted)
            {
                // ERR_ABORTED occurs during redirects or when a new navigation replaces an ongoing request. Ignore.
                return;
            }

            Dispatcher.Invoke(() =>
            {
                loggingService?.Log("NavigationFailed", new { url = e.FailedUrl, errorCode = e.ErrorCode, errorText = e.ErrorText });
                PageLoadFailed?.Invoke(this, $"Failed to load exam URL ({e.ErrorCode}): {e.ErrorText}");
            });
        };

        Content = _browser;
    }

    public void LoadUrl(string url)
    {
        _browser?.Load(url);
    }

    public void TerminateSession()
    {
        _browser?.Load("about:blank");
    }
}
