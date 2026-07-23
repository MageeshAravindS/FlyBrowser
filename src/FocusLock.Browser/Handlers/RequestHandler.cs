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

            // Always allow localhost, 127.0.0.1, and Google OAuth authentication domains
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("::1") ||
                host.EndsWith("google.com", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("googleapis.com", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("gstatic.com", StringComparison.OrdinalIgnoreCase))
            {
                _loggingService?.Log("NavigationStarted", new { url = request.Url, isRedirect, userGesture });
                return false;
            }

            if (_allowedDomains.Count > 0)
            {
                bool isAllowed = _allowedDomains.Any(domain =>
                {
                    if (string.IsNullOrWhiteSpace(domain)) return false;
                    string cleanDomain = domain.Trim();
                    string targetHost = cleanDomain;
                    if (Uri.TryCreate(cleanDomain.StartsWith("http") ? cleanDomain : "http://" + cleanDomain, UriKind.Absolute, out var dUri))
                    {
                        targetHost = dUri.Host;
                    }
                    return host.Equals(targetHost, StringComparison.OrdinalIgnoreCase) ||
                           host.EndsWith("." + targetHost, StringComparison.OrdinalIgnoreCase) ||
                           (host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) && targetHost.Equals("localhost", StringComparison.OrdinalIgnoreCase)) ||
                           (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) && targetHost.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase));
                });

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
