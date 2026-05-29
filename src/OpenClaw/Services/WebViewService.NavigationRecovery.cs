// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.Web.WebView2.Core;

namespace OpenClaw.Services;

public partial class WebViewService
{
    private async Task<AutoRetryOutcome> TryAutoRetryAfterConnectionErrorAsync(
        CoreWebView2 sender,
        CoreWebView2NavigationCompletedEventArgs args,
        int hostGeneration)
    {
        if (_retryCount >= MaxRetries || string.IsNullOrEmpty(_lastNavigatedUrl))
        {
            return AutoRetryOutcome.NotAttempted;
        }

        _retryCount++;
        var retryGeneration = _generations.Current;
        var retryNavigationId = args.NavigationId;
        var navigationCancellation = _navigationCancellation;
        using var navigationLease = navigationCancellation?.TryAcquire();
        if (navigationLease is null)
        {
            return AutoRetryOutcome.Stale;
        }

        var token = navigationLease.Token;
        _logger.Info($"Auto-retry {_retryCount}/{MaxRetries} in {RetryDelay.TotalSeconds}s...");

        try
        {
            await Task.Delay(RetryDelay, token);
        }
        catch (TaskCanceledException)
        {
            _logger.Info("Auto-retry cancelled (new navigation started).");
            return AutoRetryOutcome.Stale;
        }

        var coreWebView = GetCoreWebView();
        if (_isDisposed ||
            !_generations.IsCurrent(retryGeneration) ||
            retryNavigationId != _currentNavigationId ||
            !IsCurrentHost(hostGeneration) ||
            coreWebView is null ||
            string.IsNullOrEmpty(_lastNavigatedUrl))
        {
            return AutoRetryOutcome.Stale;
        }

        var navigationGeneration = PrepareNavigationStart();
        var previousSource = coreWebView.Source;
        ObserveNavigationStartTimeout(navigationGeneration, _lastNavigatedUrl, previousSource);
        if (!TryNavigateCoreWebView(coreWebView, _lastNavigatedUrl, "Auto-retry"))
        {
            CancelNavigationStartWatchdog();
            CancelNavigationCancellation();
            return AutoRetryOutcome.Failed;
        }

        return AutoRetryOutcome.Started;
    }

    private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args, int hostGeneration)
    {
        if (!IsCurrentHost(hostGeneration) || _coreWebView is null)
        {
            return;
        }

        CancelActiveNavigation();
        _statusInspector.SetUnavailableSnapshot("Browser process failed.");
        _logger.Error($"WebView2 process failed: {args.Reason} ({args.ProcessFailedKind})");
        SetState(ConnectionState.Error);
        NavigationErrorOccurred?.Invoke($"Browser process failed: {args.Reason}");
    }

    private enum AutoRetryOutcome
    {
        NotAttempted,
        Started,
        Stale,
        Failed,
    }
}
