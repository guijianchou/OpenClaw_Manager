// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
        _ = RecreateWebViewAsync();
    }

    private void RecordInstrumentationEvent(string eventName, object? context = null)
    {
        _lastInstrumentationEvent = eventName;
        App.Logger.Info(eventName, context);

        if (ViewModel.Coordinator is not null)
        {
            ViewModel.Coordinator.UpdateInstrumentation(
                totalWebViewRecreations: _webViewRecreationService.TotalRecreations,
                mergedWebViewRecreationRequests: _webViewRecreationService.MergedRequests,
                lastInstrumentationEvent: _lastInstrumentationEvent);
        }
    }

    private void ScheduleWebViewRecreation(string reason)
    {
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

    private async Task RecreateWebViewAsync()
    {
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
                if (!_webViewRecreationService.CanAttemptInLoop())
                {
                    RecordInstrumentationEvent("webview.recreation.circuit_breaker_tripped_in_loop", new
                    {
                        lastReason = _webViewRecreationService.LastReason,
                        total = _webViewRecreationService.TotalRecreations
                    });
                    break;
                }

                var nextWebView = new WebView2
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };

                foreach (var child in WebViewHost.Children.OfType<WebView2>().ToArray())
                {
                    child.Close();
                }

                WebViewHost.Children.Clear();
                WebViewHost.Children.Add(nextWebView);

                _webViewRecreationService.RecordAttempt();
                RecordInstrumentationEvent("webview.recreation.initializing", new
                {
                    reason = _webViewRecreationService.LastReason,
                    total = _webViewRecreationService.TotalRecreations
                });
                await ViewModel.InitializeWebViewAsync(nextWebView);
            }
            while (_webViewRecreationService.TryConsumeQueued(out _));
        }
        catch (Exception ex)
        {
            App.Logger.Error($"Failed to recreate WebView2 host: {ex.Message}");
        }
        finally
        {
            var finished = _webViewRecreationService.Finish();

            if (finished.PendingReason is not null)
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
}
