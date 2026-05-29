// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace OpenClaw.Services;

public partial class WebViewService
{
    private bool TryNavigateCoreWebView(CoreWebView2 coreWebView, string url, string context)
    {
        try
        {
            coreWebView.Navigate(url);
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            _logger.Warning($"{context} skipped because CoreWebView2 became unavailable: {ex.Message}");
            return false;
        }
    }

    private bool TryReloadCoreWebView(CoreWebView2 coreWebView)
    {
        try
        {
            coreWebView.Reload();
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            _logger.Warning($"Reload skipped because CoreWebView2 became unavailable: {ex.Message}");
            return false;
        }
    }
}
