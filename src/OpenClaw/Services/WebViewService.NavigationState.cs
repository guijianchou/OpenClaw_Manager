// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.Web.WebView2.Core;

namespace OpenClaw.Services;

public partial class WebViewService
{
    private bool IsRecoveredNavigationCompletionForPendingTarget(CoreWebView2 sender, int hostGeneration)
    {
        if (!TryGetActiveNavigationStartWatchdog(
                out var navigationGeneration,
                out var expectedUrl,
                out var previousSource))
        {
            return false;
        }

        if (!IsCurrentHost(hostGeneration) || !_generations.IsCurrent(navigationGeneration))
        {
            return false;
        }

        var currentSource = sender.Source;
        if (string.IsNullOrWhiteSpace(currentSource))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(expectedUrl))
        {
            return !AreNavigationUrlsEquivalent(currentSource, previousSource);
        }

        if (!AreNavigationUrlsEquivalent(currentSource, expectedUrl))
        {
            return false;
        }

        // Reloads and same-URL navigations have the pending target equal to the previous source.
        return string.IsNullOrWhiteSpace(previousSource) ||
            AreNavigationUrlsEquivalent(expectedUrl, previousSource) ||
            !AreNavigationUrlsEquivalent(currentSource, previousSource);
    }

    private static bool AreNavigationUrlsEquivalent(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Uri.TryCreate(left, UriKind.Absolute, out var leftUri) &&
            Uri.TryCreate(right, UriKind.Absolute, out var rightUri) &&
            Uri.Compare(
                leftUri,
                rightUri,
                UriComponents.HttpRequestUrl,
                UriFormat.SafeUnescaped,
                StringComparison.OrdinalIgnoreCase) == 0;
    }

    private int PrepareNavigationStart()
    {
        CancelNavigationStartWatchdog();
        CancelNavigationCompletionWatchdog();
        CancelStatusProbeLoop();
        InvalidateControlUiInspectionCache();
        var generation = _generations.Next();
        _currentNavigationId = NoCurrentNavigationId;
        _messageOwnership.BeginNavigation();
        ReplaceNavigationCancellation();
        _lastReportedIssueKey = null;
        _heartbeatConnectingCount = 0;
        _lastHeartbeatObservationKey = null;
        return generation;
    }

    private void CancelActiveNavigation()
    {
        CancelNavigationStartWatchdog();
        CancelNavigationCompletionWatchdog();
        CancelStatusProbeLoop();
        InvalidateControlUiInspectionCache();
        _generations.Next();
        _currentNavigationId = NoCurrentNavigationId;
        _messageOwnership.BeginNavigation();
        CancelNavigationCancellation();
        _lastReportedIssueKey = null;
    }

    private void ReplaceNavigationCancellation()
    {
        var previous = _navigationCancellation;
        _navigationCancellation = new NavigationCancellationScope();
        previous?.CancelAndRetire();
    }

    private void CancelNavigationCancellation()
    {
        var cancellation = _navigationCancellation;
        _navigationCancellation = null;
        cancellation?.CancelAndRetire();
    }
}
