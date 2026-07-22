using System;
using System.Collections.Generic;
using System.Linq;
using CefSharp;
using CefSharp.Handler;
using FocusLock.Logging;

namespace FocusLock.Browser.Handlers;

public class RequestHandler : CefSharp.Handler.RequestHandler
{
    private readonly List<string> _allowedDomains;
    private readonly LoggingService? _loggingService;

    public RequestHandler(List<string> allowedDomains, LoggingService? loggingService = null)
    {
        _allowedDomains = allowedDomains ?? new List<string>();
        _loggingService = loggingService;
    }

    protected override bool OnBeforeBrowse(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool userGesture, bool isRedirect)
    {
        if (string.IsNullOrWhiteSpace(request.Url) || request.Url.StartsWith("about:", StringComparison.OrdinalIgnoreCase) || request.Url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
        {
            string host = uri.Host;

            if (_allowedDomains.Count > 0)
            {
                bool isAllowed = _allowedDomains.Any(domain =>
                    host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));

                if (!isAllowed)
                {
                    _loggingService?.Log("NavigationBlocked", new
                    {
                        url = request.Url,
                        host,
                        reason = "Domain not in allowed list"
                    });
                    return true;
                }
            }
        }

        _loggingService?.Log("NavigationStarted", new { url = request.Url, isRedirect, userGesture });
        return false;
    }
}
