// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace OpenClaw.Services;

public partial class WebViewService
{
    private TypedEventHandler<CoreWebView2, CoreWebView2NavigationStartingEventArgs> CreateNavigationStartingHandler(int hostGeneration)
    {
        return (sender, args) => OnNavigationStarting(sender, args, hostGeneration);
    }

    private TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs> CreateNavigationCompletedHandler(int hostGeneration)
    {
        return (sender, args) => OnNavigationCompleted(sender, args, hostGeneration);
    }

    private TypedEventHandler<CoreWebView2, CoreWebView2ProcessFailedEventArgs> CreateProcessFailedHandler(int hostGeneration)
    {
        return (sender, args) => OnProcessFailed(sender, args, hostGeneration);
    }

    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args, int hostGeneration)
    {
        var coreWebView = _coreWebView;
        if (coreWebView is null || !IsCurrentHost(hostGeneration))
        {
            return;
        }

        CancelNavigationStartWatchdog();
        var navigationGeneration = PrepareNavigationStart();
        _currentNavigationId = args.NavigationId;
        _statusInspector.SetLoadingSnapshot(args.Uri);
        SetState(ConnectionState.Loading);
        ObserveNavigationCompletionTimeout(args.NavigationId, navigationGeneration, args.Uri);
        LogLifecycleEventOnce("navigation.starting", new { uri = args.Uri });
    }

    private async void OnNavigationCompleted(
        CoreWebView2 sender,
        CoreWebView2NavigationCompletedEventArgs args,
        int hostGeneration)
    {
        try
        {
            await HandleNavigationCompletedAsync(sender, args, hostGeneration);
        }
        catch (OperationCanceledException) when (_isDisposed)
        {
        }
        catch (ObjectDisposedException) when (_isDisposed)
        {
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                var message = $"Navigation completion handling failed: {ex.Message}";
                _logger.Warning(message);
                _statusInspector.SetUnavailableSnapshot(message);
                SetState(ConnectionState.Error);
                NavigationErrorOccurred?.Invoke(message);
            }
        }
    }

    private async Task HandleNavigationCompletedAsync(
        CoreWebView2 sender,
        CoreWebView2NavigationCompletedEventArgs args,
        int hostGeneration)
    {
        if (!IsCurrentHost(hostGeneration) || _coreWebView is null)
        {
            return;
        }

        if (!TryClaimNavigationCompleted(sender, args, hostGeneration))
        {
            return;
        }

        CancelNavigationStartWatchdog();
        CancelNavigationCompletionWatchdog();
        if (args.IsSuccess)
        {
            _retryCount = 0;
            var completionGeneration = _generations.Current;
            var navigationCancellation = TryEnsureNavigationCancellationForRecoveredCompletion(
                args.NavigationId,
                completionGeneration,
                hostGeneration);
            if (navigationCancellation is null)
            {
                return;
            }

            var navigationLease = navigationCancellation.TryAcquire();
            if (navigationLease is null)
            {
                return;
            }

            try
            {
                var pageTokenAccepted = await CaptureCurrentPageTokenAsync(
                    sender,
                    args.NavigationId,
                    completionGeneration,
                    hostGeneration,
                    navigationLease.Token);
                if (!IsCurrentNavigation(args.NavigationId, completionGeneration, hostGeneration))
                {
                    return;
                }

                if (!pageTokenAccepted)
                {
                    ObservePageTokenCaptureRetry(sender, args.NavigationId, completionGeneration, hostGeneration, navigationCancellation);
                }
                else
                {
                    ObserveSessionReadyReportRequest(sender, args.NavigationId, completionGeneration, hostGeneration, navigationCancellation);
                }
            }
            finally
            {
                navigationLease.Dispose();
            }

            _statusInspector.SetPageLoadedSnapshot(sender.Source);
            StartStatusProbeLoop();
            LogLifecycleEventOnce("navigation.completed", new { uri = sender.Source });
            NavigationTimeoutRecovered?.Invoke();
            NavigationCompleted?.Invoke(sender.Source);
        }
        else
        {
            CancelStatusProbeLoop();
            _logger.Warning($"Navigation failed: {args.WebErrorStatus}");

            var isConnectionError = args.WebErrorStatus is
                CoreWebView2WebErrorStatus.ConnectionAborted or
                CoreWebView2WebErrorStatus.ConnectionReset or
                CoreWebView2WebErrorStatus.Disconnected or
                CoreWebView2WebErrorStatus.Timeout or
                CoreWebView2WebErrorStatus.ServerUnreachable or
                CoreWebView2WebErrorStatus.HostNameNotResolved;

            if (isConnectionError)
            {
                SetState(ConnectionState.Reconnecting);

                var autoRetryOutcome = await TryAutoRetryAfterConnectionErrorAsync(sender, args, hostGeneration);
                if (autoRetryOutcome == AutoRetryOutcome.Started)
                {
                    return; // don't fire error event for auto-retries
                }

                if (autoRetryOutcome == AutoRetryOutcome.Stale)
                {
                    return;
                }

                if (autoRetryOutcome == AutoRetryOutcome.Failed)
                {
                    SetState(ConnectionState.Error);
                    NavigationErrorOccurred?.Invoke("Auto-retry failed before WebView2 was ready.");
                    return;
                }
            }

            SetState(args.WebErrorStatus switch
            {
                CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect or
                CoreWebView2WebErrorStatus.CertificateExpired or
                CoreWebView2WebErrorStatus.CertificateRevoked or
                CoreWebView2WebErrorStatus.CertificateIsInvalid => ConnectionState.AuthFailed,
                _ when isConnectionError => ConnectionState.Error,
                _ => ConnectionState.Error,
            });
            NavigationErrorOccurred?.Invoke($"Navigation error: {args.WebErrorStatus}");
        }
    }

    private bool TryClaimNavigationCompleted(
        CoreWebView2 sender,
        CoreWebView2NavigationCompletedEventArgs args,
        int hostGeneration)
    {
        if (!IsCurrentHost(hostGeneration) || _coreWebView is null)
        {
            return false;
        }

        if (args.NavigationId == _currentNavigationId)
        {
            return true;
        }

        if (_currentNavigationId == NoCurrentNavigationId &&
            IsRecoveredNavigationCompletionForPendingTarget(sender, hostGeneration))
        {
            CancelNavigationStartWatchdog();
            _currentNavigationId = args.NavigationId;
            LogLifecycleEventOnce(
                "navigation.starting.recovered_from_completion",
                new
                {
                    uri = sender.Source,
                    args.NavigationId
                });
            return true;
        }

        return false;
    }

    private NavigationCancellationScope? TryEnsureNavigationCancellationForRecoveredCompletion(
        ulong navigationId,
        int generation,
        int hostGeneration)
    {
        var navigationCancellation = _navigationCancellation;
        if (navigationCancellation is not null)
        {
            return navigationCancellation;
        }

        if (!IsCurrentNavigation(navigationId, generation, hostGeneration))
        {
            return null;
        }

        _logger.Info("navigation.completion.recovered_after_timeout", new
        {
            navigationId,
            generation
        });
        ReplaceNavigationCancellation();
        return _navigationCancellation;
    }

}
