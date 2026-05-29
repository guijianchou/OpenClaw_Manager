// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace OpenClaw.Services;

public partial class WebViewService
{
    private TypedEventHandler<CoreWebView2, CoreWebView2WebMessageReceivedEventArgs> CreateWebMessageReceivedHandler(int hostGeneration)
    {
        return (sender, args) => OnWebMessageReceived(sender, args, hostGeneration);
    }

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args, int hostGeneration)
    {
        try
        {
            if (!IsCurrentHost(hostGeneration))
            {
                return;
            }

            var message = args.WebMessageAsJson;
            using var document = System.Text.Json.JsonDocument.Parse(message);
            var root = document.RootElement;
            if (!_messageOwnership.TryCaptureCurrentVersion(args, root, out var pageVersion))
            {
                return;
            }

            if (!_statusInspector.TryApplyHostMessage(message, pageVersion, out var snapshot))
            {
                return;
            }

            if (!_messageOwnership.IsCurrentAcceptedPageVersion(pageVersion))
            {
                return;
            }

            ApplyControlUiSnapshot(snapshot, raiseIssueEvent: true);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to process Control UI status message: {ex.Message}");
        }
    }
}
