// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenClaw.Helpers;

namespace OpenClaw;

public sealed partial class MainWindow
{
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.IsRunIndicatorsAnimating))
        {
            UpdateRunIndicatorAnimationState();
        }
        else if (e.PropertyName == nameof(ViewModel.WorkStatusText))
        {
            UpdateTrayStatus();
        }
        else if (e.PropertyName == nameof(ViewModel.LoadingVisibility))
        {
            UpdateLoadingRingVisibility();
        }
    }

    private void UpdateRunIndicatorAnimationState()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (ViewModel.IsRunIndicatorsAnimating && !_isWindowHidden)
            {
                if (!_runIndicatorTimer.IsRunning)
                {
                    _runIndicatorTimer.Start();
                }
            }
            else
            {
                _runIndicatorTimer.Stop();
            }
        });
    }

    private void OnRunIndicatorTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        ViewModel.AdvanceRunIndicators();
    }

    private void OnWebViewRecreationTimerTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        _webViewRecreationTimer.Stop();
        var recreationTask = RecreateWebViewAsync(_windowLifetimeCts.Token);
        _webViewRecreationTask = recreationTask;
        _ = ObserveWebViewRecreationAsync(recreationTask);
    }

    private void RecordInstrumentationEvent(string eventName, object? context = null)
    {
        if (_isClosing || _windowLifetimeCts.IsCancellationRequested)
        {
            return;
        }

        _lastInstrumentationEvent = eventName;
        App.Logger.Info(eventName, context);

        ViewModel.UpdateShellInstrumentation(
            _lastInstrumentationEvent,
            _webViewRecreationService.TotalRecreations,
            _webViewRecreationService.MergedRequests);
    }

    private void ScheduleWebViewRecreation(string reason)
    {
        if (_isClosing || _windowLifetimeCts.IsCancellationRequested)
        {
            return;
        }

        if (!CanInitializeWebViewHost())
        {
            _deferredWebViewRecreationReason = reason;
            RecordInstrumentationEvent("webview.recreation.deferred_until_visible_layout", CreateWebViewHostLayoutContext());
            return;
        }

        var scheduled = _webViewRecreationService.Schedule(reason);

        RecordInstrumentationEvent("webview.recreation.queued", new
        {
            reason = scheduled.Reason,
            isRecreating = scheduled.IsRecreating,
            merged = scheduled.MergedRequests
        });

        if (!scheduled.ShouldStartTimer)
        {
            return;
        }

        if (_webViewRecreationTimer.IsRunning)
        {
            _webViewRecreationTimer.Stop();
        }

        _webViewRecreationTimer.Start();
    }

    private async Task RecreateWebViewAsync(CancellationToken cancellationToken)
    {
        if (_isClosing || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var begin = _webViewRecreationService.TryBegin(WebViewHost.Children.Count > 0);
        if (begin.IsCircuitBreakerTripped)
        {
            RecordInstrumentationEvent("webview.recreation.circuit_breaker_tripped", new
            {
                lastReason = begin.LastReason,
                total = begin.TotalRecreations
            });
            ViewModel.ShowCircuitBreakerError();
            return;
        }

        if (!begin.ShouldBegin || begin.Reason is null)
        {
            return;
        }

        if (string.Equals(begin.Reason, "implicit_initial_load", StringComparison.Ordinal))
        {
            RecordInstrumentationEvent("webview.recreation.recovered_missing_reason", new
            {
                reason = begin.Reason,
                lastReason = begin.LastReason
            });
        }

        RecordInstrumentationEvent("webview.recreation.started", new { reason = begin.Reason });

        try
        {
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_webViewRecreationService.CanAttemptInLoop())
                {
                    RecordInstrumentationEvent("webview.recreation.circuit_breaker_tripped_in_loop", new
                    {
                        lastReason = _webViewRecreationService.LastReason,
                        total = _webViewRecreationService.TotalRecreations
                    });
                    ViewModel.ShowCircuitBreakerError();
                    break;
                }

                var nextWebView = new WebView2
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };

                ViewModel.DetachWebViewHost();
                foreach (var child in WebViewHost.Children.OfType<WebView2>().ToArray())
                {
                    child.Close();
                }

                WebViewHost.Children.Clear();
                WebViewHost.Children.Add(nextWebView);
                if (!await WaitForWebViewHostLayoutAsync(nextWebView, cancellationToken))
                {
                    _deferredWebViewRecreationReason = _webViewRecreationService.LastReason ?? begin.Reason;
                    RecordInstrumentationEvent("webview.recreation.deferred_until_visible_layout", CreateWebViewHostLayoutContext());
                    WebViewHost.Children.Clear();
                    nextWebView.Close();
                    break;
                }

                _webViewRecreationService.RecordAttempt();
                RecordInstrumentationEvent("webview.recreation.initializing", new
                {
                    reason = _webViewRecreationService.LastReason,
                    total = _webViewRecreationService.TotalRecreations
                });
                await ViewModel.InitializeWebViewAsync(nextWebView);
                cancellationToken.ThrowIfCancellationRequested();
            }
            while (_webViewRecreationService.TryConsumeQueued(out _));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!_isClosing)
            {
                App.Logger.Info("WebView2 recreation cancelled.");
            }
        }
        catch (Exception ex)
        {
            if (!_isClosing && !cancellationToken.IsCancellationRequested)
            {
                App.Logger.Error($"Failed to recreate WebView2 host: {ex.Message}");
            }
        }
        finally
        {
            var finished = _webViewRecreationService.Finish();

            if (!_isClosing && !cancellationToken.IsCancellationRequested && finished.PendingReason is not null)
            {
                ScheduleWebViewRecreation(finished.PendingReason);
            }

            RecordInstrumentationEvent("webview.recreation.finished", new
            {
                lastReason = finished.LastReason,
                pendingReason = finished.PendingReason,
                total = finished.TotalRecreations,
                merged = finished.MergedRequests
            });
        }
    }

    private async Task<bool> WaitForWebViewHostLayoutAsync(WebView2 webView, CancellationToken cancellationToken)
    {
        if (webView.IsLoaded && HasUsableWebViewHostLayout())
        {
            RecordInstrumentationEvent("webview.host.layout_ready", CreateWebViewHostLayoutContext());
            return true;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void TryComplete()
        {
            if (webView.IsLoaded && HasUsableWebViewHostLayout())
            {
                completion.TrySetResult();
            }
        }

        void OnLoaded(object sender, RoutedEventArgs args) => TryComplete();

        void OnSizeChanged(object sender, SizeChangedEventArgs args) => TryComplete();

        webView.Loaded += OnLoaded;
        WebViewHost.SizeChanged += OnSizeChanged;

        try
        {
            TryComplete();

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                timeout.Token,
                cancellationToken);

            try
            {
                await completion.Task.WaitAsync(linkedCancellation.Token);
                RecordInstrumentationEvent("webview.host.layout_ready", CreateWebViewHostLayoutContext());
                return true;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                App.Logger.Warning("webview.host.layout_wait_timeout", CreateWebViewHostLayoutContext());
                return false;
            }
        }
        finally
        {
            webView.Loaded -= OnLoaded;
            WebViewHost.SizeChanged -= OnSizeChanged;
        }
    }

    private bool HasUsableWebViewHostLayout()
    {
        return WebViewHost.ActualSize.X > 0 &&
            WebViewHost.ActualSize.Y > 0 &&
            WebViewHost.Visibility == Visibility.Visible;
    }

    private bool CanInitializeWebViewHost()
    {
        return !_isCompactMode &&
            !_isWindowHidden &&
            !WindowFrameHelper.IsWindowMinimized(this) &&
            HasUsableWebViewHostLayout();
    }

    private void OnWebViewHostSizeChanged(object sender, SizeChangedEventArgs args)
    {
        ResumeDeferredWebViewRecreationIfReady();
    }

    private void ResumeDeferredWebViewRecreationIfReady()
    {
        if (string.IsNullOrEmpty(_deferredWebViewRecreationReason) || !CanInitializeWebViewHost())
        {
            return;
        }

        var reason = _deferredWebViewRecreationReason;
        _deferredWebViewRecreationReason = null;
        ScheduleWebViewRecreation("visible_layout_ready");
        RecordInstrumentationEvent("webview.recreation.deferred_resumed", new
        {
            reason
        });
    }

    private object CreateWebViewHostLayoutContext()
    {
        return new
        {
            width = WebViewHost.ActualSize.X,
            height = WebViewHost.ActualSize.Y,
            visibility = WebViewHost.Visibility.ToString()
        };
    }

    private async Task ObserveWebViewRecreationAsync(Task recreationTask)
    {
        try
        {
            await recreationTask;
        }
        catch (Exception ex)
        {
            if (!_isClosing && !_windowLifetimeCts.IsCancellationRequested)
            {
                App.Logger.Error($"Unobserved WebView2 recreation failure: {ex.Message}");
            }
        }
        finally
        {
            if (ReferenceEquals(_webViewRecreationTask, recreationTask))
            {
                _webViewRecreationTask = null;
            }
        }
    }
}
