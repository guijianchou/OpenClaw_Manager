// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace OpenClaw.Services;

internal sealed partial class WebViewStatusInspector
{
    private Task<ControlUiProbeSnapshot> InspectAsync(
        CancellationToken cancellationToken,
        int generation,
        bool publishSnapshot)
    {
        Interlocked.Increment(ref _totalControlUiInspectionRequests);
        if (cancellationToken.IsCancellationRequested || !_generations.IsCurrent(generation))
        {
            return Task.FromResult(ControlUiProbeSnapshot.Unknown);
        }

        var coreWebView = _getCoreWebView();
        if (coreWebView is null)
        {
            return Task.FromResult(ControlUiProbeSnapshot.Unavailable("WebView2 is not initialized."));
        }

        var pageVersion = _messageOwnership.CaptureAcceptedPageVersion();
        if (pageVersion == 0)
        {
            return Task.FromResult(GetLatestSnapshotForGenerationOrUnknown(generation));
        }

        lock (_inspectionGate)
        {
            if (_inFlightInspectionTask is not null &&
                _inFlightInspectionGeneration == generation &&
                _inFlightInspectionPageVersion == pageVersion)
            {
                var coalescedCount = Interlocked.Increment(ref _coalescedControlUiInspectionRequests);
                if (ShouldLogInspectionInstrumentationCount(coalescedCount))
                {
                    _logger.Info("webview.inspect.coalesced", new
                    {
                        requested = TotalRequests,
                        coalesced = coalescedCount
                    });
                }

                return WaitForInspectionAsync(
                    _inFlightInspectionTask,
                    cancellationToken,
                    publishSnapshot ? TrackInFlightInspectionPublishWaiter(_inFlightInspectionId, cancellationToken) : null);
            }

            if (_latestControlUiSnapshot != ControlUiProbeSnapshot.Unknown &&
                _latestControlUiSnapshotGeneration == generation &&
                _latestControlUiSnapshotPageVersion == pageVersion &&
                DateTimeOffset.UtcNow - _lastControlUiInspectionAt < InspectionReuseWindow)
            {
                var cachedCount = Interlocked.Increment(ref _cachedControlUiInspectionRequests);
                if (ShouldLogInspectionInstrumentationCount(cachedCount))
                {
                    _logger.Info("webview.inspect.cached", new
                    {
                        requested = TotalRequests,
                        cached = cachedCount
                    });
                }

                return Task.FromResult(_latestControlUiSnapshot);
            }

            var inspectionSource = new TaskCompletionSource<ControlUiProbeSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _inFlightInspectionTask = inspectionSource.Task;
            _inFlightInspectionGeneration = generation;
            _inFlightInspectionPageVersion = pageVersion;
            _inFlightInspectionId++;
            _inFlightInspectionPublishWaiters = 0;
            var inspectionId = _inFlightInspectionId;
            _ = CompleteControlUiInspectionAsync(coreWebView, inspectionSource, generation, pageVersion, inspectionId);
            return WaitForInspectionAsync(
                inspectionSource.Task,
                cancellationToken,
                publishSnapshot ? TrackInFlightInspectionPublishWaiter(inspectionId, cancellationToken) : null);
        }
    }

    private static Task<ControlUiProbeSnapshot> WaitForInspectionAsync(
        Task<ControlUiProbeSnapshot> task,
        CancellationToken cancellationToken)
    {
        return cancellationToken.CanBeCanceled ? task.WaitAsync(cancellationToken) : task;
    }

    private static async Task<ControlUiProbeSnapshot> WaitForInspectionAsync(
        Task<ControlUiProbeSnapshot> task,
        CancellationToken cancellationToken,
        InspectionPublishWaiter? waiter)
    {
        try
        {
            return await WaitForInspectionAsync(task, cancellationToken);
        }
        finally
        {
            waiter?.Dispose();
        }
    }

    private async Task CompleteControlUiInspectionAsync(
        CoreWebView2 coreWebView,
        TaskCompletionSource<ControlUiProbeSnapshot> inspectionSource,
        int generation,
        int pageVersion,
        int inspectionId)
    {
        try
        {
            inspectionSource.TrySetResult(await ExecuteControlUiInspectionAsync(
                coreWebView,
                generation,
                pageVersion,
                inspectionId));
        }
        catch (OperationCanceledException)
        {
            inspectionSource.TrySetResult(ControlUiProbeSnapshot.Unknown);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to inspect hosted UI state: {ex.Message}");
            inspectionSource.TrySetResult(ControlUiProbeSnapshot.Unavailable(ex.Message));
        }
        finally
        {
            lock (_inspectionGate)
            {
                if (ReferenceEquals(_inFlightInspectionTask, inspectionSource.Task))
                {
                    _inFlightInspectionTask = null;
                    _inFlightInspectionPageVersion = 0;
                    _inFlightInspectionId++;
                    _inFlightInspectionPublishWaiters = 0;
                }
            }
        }
    }

    private static bool ShouldLogInspectionInstrumentationCount(int count)
    {
        return count == 1 || count % 25 == 0;
    }

    private async Task<ControlUiProbeSnapshot> ExecuteControlUiInspectionAsync(
        CoreWebView2 coreWebView,
        int generation,
        int pageVersion,
        int inspectionId)
    {
        try
        {
            if (!IsCurrentInspectionTarget(generation, pageVersion))
            {
                return ControlUiProbeSnapshot.Unknown;
            }

            var rawResult = await ExecuteStatusScriptWithTimeoutAsync(coreWebView);
            if (!IsCurrentInspectionTarget(generation, pageVersion))
            {
                return ControlUiProbeSnapshot.Unknown;
            }

            _lastControlUiInspectionAt = DateTimeOffset.UtcNow;

            var payload = JsonSerializer.Deserialize<string>(rawResult);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return GetLatestSnapshotForGenerationOrUnknown(generation, pageVersion);
            }

            var snapshot = ParseControlUiSnapshot(payload);
            TryPublishInspectionSnapshot(snapshot, generation, pageVersion, inspectionId);

            return snapshot;
        }
        catch (OperationCanceledException)
        {
            return ControlUiProbeSnapshot.Unknown;
        }
        catch (TimeoutException)
        {
            var timeoutCount = Interlocked.Increment(ref _timedOutControlUiInspectionRequests);
            if (ShouldLogInspectionInstrumentationCount(timeoutCount))
            {
                _logger.Warning(
                    "webview.inspect.timeout",
                    new
                    {
                        timeoutSeconds = InspectionTimeout.TotalSeconds,
                        timedOut = timeoutCount,
                        generation
                    });
            }

            _lastControlUiInspectionAt = DateTimeOffset.UtcNow;
            var snapshot = ControlUiProbeSnapshot.Unavailable("Control UI inspection timed out.");
            TryPublishInspectionSnapshot(snapshot, generation, pageVersion, inspectionId);
            return snapshot;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to inspect hosted UI state: {ex.Message}");
            _lastControlUiInspectionAt = DateTimeOffset.UtcNow;
            var snapshot = ControlUiProbeSnapshot.Unavailable(ex.Message);
            TryPublishInspectionSnapshot(snapshot, generation, pageVersion, inspectionId);
            return snapshot;
        }
    }

    private bool TryPublishInspectionSnapshot(
        ControlUiProbeSnapshot snapshot,
        int generation,
        int pageVersion,
        int inspectionId)
    {
        return HasActiveInFlightPublishWaiter(inspectionId) &&
            IsCurrentInspectionTarget(generation, pageVersion) &&
            ApplyControlUiSnapshot(snapshot, generation, pageVersion, notifySnapshotUpdated: true);
    }

    private bool IsCurrentInspectionTarget(int generation, int pageVersion)
    {
        return _generations.IsCurrent(generation) &&
            _messageOwnership.IsCurrentAcceptedPageVersion(pageVersion);
    }

    private InspectionPublishWaiter TrackInFlightInspectionPublishWaiter(int inspectionId, CancellationToken cancellationToken)
    {
        lock (_inspectionGate)
        {
            if (_inFlightInspectionId == inspectionId)
            {
                _inFlightInspectionPublishWaiters++;
            }
        }

        return new InspectionPublishWaiter(this, inspectionId, cancellationToken);
    }

    private void ReleaseInFlightInspectionPublishWaiter(int inspectionId)
    {
        lock (_inspectionGate)
        {
            if (_inFlightInspectionId == inspectionId && _inFlightInspectionPublishWaiters > 0)
            {
                _inFlightInspectionPublishWaiters--;
            }
        }
    }

    private bool HasActiveInFlightPublishWaiter(int inspectionId)
    {
        lock (_inspectionGate)
        {
            return _inFlightInspectionId == inspectionId && _inFlightInspectionPublishWaiters > 0;
        }
    }

    private sealed class InspectionPublishWaiter : IDisposable
    {
        private readonly WebViewStatusInspector _owner;
        private readonly int _inspectionId;
        private readonly CancellationTokenRegistration _cancellationRegistration;
        private int _released;

        public InspectionPublishWaiter(WebViewStatusInspector owner, int inspectionId, CancellationToken cancellationToken)
        {
            _owner = owner;
            _inspectionId = inspectionId;
            if (cancellationToken.CanBeCanceled)
            {
                _cancellationRegistration = cancellationToken.Register(
                    static state => ((InspectionPublishWaiter)state!).Release(),
                    this);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                Release();
            }
        }

        public void Dispose()
        {
            Release();
            _cancellationRegistration.Dispose();
        }

        private void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _owner.ReleaseInFlightInspectionPublishWaiter(_inspectionId);
            }
        }
    }
}
