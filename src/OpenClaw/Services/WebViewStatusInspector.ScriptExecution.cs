// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.Web.WebView2.Core;

namespace OpenClaw.Services;

internal sealed partial class WebViewStatusInspector
{
    private static async Task<string> ExecuteStatusScriptWithTimeoutAsync(CoreWebView2 coreWebView)
    {
        using var timeout = new CancellationTokenSource(InspectionTimeout);

        try
        {
            return await coreWebView.ExecuteScriptAsync(WebViewStatusInspectionScripts.Inspect)
                .AsTask(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"Control UI inspection exceeded {InspectionTimeout.TotalSeconds:0.#}s.");
        }
    }
}
