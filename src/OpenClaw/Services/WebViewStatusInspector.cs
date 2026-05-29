// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace OpenClaw.Services;

internal sealed class WebViewStatusInspector : IDisposable
{
    private const string ControlUiStatusMessageKind = "openclaw-control-ui-status";
    private static readonly TimeSpan InspectionReuseWindow = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan InspectionTimeout = TimeSpan.FromSeconds(3);

    private readonly Func<CoreWebView2?> _getCoreWebView;
    private readonly UiTaskDispatcher _uiDispatcher;
    private readonly WebViewGenerationTracker _generations;
    private readonly WebViewMessageOwnership _messageOwnership;
    private readonly IAppLogger _logger;
    private readonly object _inspectionGate = new();
    private readonly object _probeGate = new();

    private CancellationTokenSource? _statusProbeCts;
    private Task? _statusProbeTask;
    private Task<ControlUiProbeSnapshot>? _inFlightInspectionTask;
    private int _inFlightInspectionGeneration;
    private int _inFlightInspectionPageVersion;
    private int _inFlightInspectionId;
    private int _inFlightInspectionPublishWaiters;
    private DateTimeOffset _lastControlUiInspectionAt = DateTimeOffset.MinValue;
    private ControlUiProbeSnapshot _latestControlUiSnapshot = ControlUiProbeSnapshot.Unknown;
    private int _latestControlUiSnapshotGeneration;
    private int _latestControlUiSnapshotPageVersion;
    private int _totalControlUiInspectionRequests;
    private int _cachedControlUiInspectionRequests;
    private int _coalescedControlUiInspectionRequests;
    private int _timedOutControlUiInspectionRequests;

    public WebViewStatusInspector(
        Func<CoreWebView2?> getCoreWebView,
        UiTaskDispatcher uiDispatcher,
        WebViewGenerationTracker generations,
        WebViewMessageOwnership messageOwnership,
        IAppLogger logger)
    {
        _getCoreWebView = getCoreWebView;
        _uiDispatcher = uiDispatcher;
        _generations = generations;
        _messageOwnership = messageOwnership;
        _logger = logger;
    }

    public event Action<ControlUiProbeSnapshot>? SnapshotUpdated;

    public ControlUiProbeSnapshot LatestSnapshot => _latestControlUiSnapshot;

    public int TotalRequests => Volatile.Read(ref _totalControlUiInspectionRequests);

    public int CachedRequests => Volatile.Read(ref _cachedControlUiInspectionRequests);

    public int CoalescedRequests => Volatile.Read(ref _coalescedControlUiInspectionRequests);

    public Task<ControlUiProbeSnapshot> InspectAsync(
        CancellationToken cancellationToken = default,
        bool publishSnapshot = true)
    {
        return InspectAsync(cancellationToken, _generations.Current, publishSnapshot);
    }

    public void StartProbeLoop()
    {
        CancelProbeLoop();
        var cancellation = new CancellationTokenSource();
        var generation = _generations.Current;

        lock (_probeGate)
        {
            _statusProbeCts = cancellation;
            _statusProbeTask = ProbeControlUiStateAfterNavigationAsync(cancellation, generation);
        }
    }

    public void CancelProbeLoop()
    {
        lock (_probeGate)
        {
            try
            {
                _statusProbeCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The running probe owns disposal and may have finished while cancellation was requested.
            }

            _statusProbeCts = null;
            _statusProbeTask = null;
        }
    }

    public void InvalidateCache()
    {
        lock (_inspectionGate)
        {
            _lastControlUiInspectionAt = DateTimeOffset.MinValue;
            _latestControlUiSnapshotGeneration = 0;
            _latestControlUiSnapshotPageVersion = 0;
            _inFlightInspectionTask = null;
            _inFlightInspectionPageVersion = 0;
            _inFlightInspectionId++;
            _inFlightInspectionPublishWaiters = 0;
        }
    }

    public void SetLoadingSnapshot(string? uri)
    {
        ApplyControlUiSnapshot(ControlUiProbeSnapshot.Loading(uri), _generations.Current, pageVersion: 0, notifySnapshotUpdated: true);
    }

    public void SetPageLoadedSnapshot(string? uri)
    {
        ApplyControlUiSnapshot(ControlUiProbeSnapshot.PageLoaded(uri), _generations.Current, pageVersion: 0, notifySnapshotUpdated: true);
    }

    public void SetUnavailableSnapshot(string summary)
    {
        ApplyControlUiSnapshot(ControlUiProbeSnapshot.Unavailable(summary), _generations.Current, pageVersion: 0, notifySnapshotUpdated: true);
    }

    public bool TrySetUnavailableSnapshot(string summary, int generation)
    {
        return ApplyControlUiSnapshot(ControlUiProbeSnapshot.Unavailable(summary), generation, pageVersion: 0, notifySnapshotUpdated: true);
    }

    public void SetUnknownSnapshot()
    {
        ApplyControlUiSnapshot(ControlUiProbeSnapshot.Unknown, _generations.Current, pageVersion: 0, notifySnapshotUpdated: false);
    }

    public bool TryApplyHostMessage(string json, int pageVersion, out ControlUiProbeSnapshot snapshot)
    {
        snapshot = ParseControlUiSnapshot(json);
        if (snapshot.Phase == ControlUiPhase.Unknown)
        {
            return false;
        }

        if (pageVersion == 0)
        {
            return false;
        }

        return ApplyControlUiSnapshot(
            snapshot,
            _generations.Current,
            pageVersion,
            notifySnapshotUpdated: false);
    }

    public void Dispose()
    {
        CancelProbeLoop();
    }

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

    private async Task ProbeControlUiStateAfterNavigationAsync(CancellationTokenSource cancellation, int generation)
    {
        var cancellationToken = cancellation.Token;
        var delays = new[]
        {
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(8),
        };

        try
        {
            foreach (var delay in delays)
            {
                await Task.Delay(delay, cancellationToken);
                if (!_generations.IsCurrent(generation))
                {
                    return;
                }

                var snapshot = await _uiDispatcher.RunAsync(
                    () => InspectAsync(cancellationToken, generation, publishSnapshot: true),
                    cancellationToken);
                if (snapshot.IsTerminal)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when navigation changes.
        }
        catch (ObjectDisposedException)
        {
            // Expected when WebView2 is torn down during navigation/recreation.
        }
        catch (Exception ex)
        {
            if (_generations.IsCurrent(generation))
            {
                _logger.Warning($"WebView status probe loop failed: {ex.Message}");
            }
        }
        finally
        {
            lock (_probeGate)
            {
                if (ReferenceEquals(_statusProbeCts, cancellation))
                {
                    _statusProbeCts = null;
                    _statusProbeTask = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private bool ApplyControlUiSnapshot(
        ControlUiProbeSnapshot snapshot,
        int generation,
        int pageVersion,
        bool notifySnapshotUpdated)
    {
        if (!_generations.IsCurrent(generation))
        {
            return false;
        }

        if (pageVersion != 0 && !_messageOwnership.IsCurrentAcceptedPageVersion(pageVersion))
        {
            return false;
        }

        var shouldNotify = notifySnapshotUpdated &&
            !EqualityComparer<ControlUiProbeSnapshot>.Default.Equals(_latestControlUiSnapshot, snapshot);
        _latestControlUiSnapshot = snapshot;
        _latestControlUiSnapshotGeneration = generation;
        _latestControlUiSnapshotPageVersion = pageVersion;

        if (snapshot.IsTerminal)
        {
            CancelProbeLoop();
        }

        if (shouldNotify)
        {
            SnapshotUpdated?.Invoke(snapshot);
        }

        return true;
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

    private ControlUiProbeSnapshot GetLatestSnapshotForGenerationOrUnknown(int generation, int pageVersion = 0)
    {
        if (_latestControlUiSnapshotGeneration != generation)
        {
            return ControlUiProbeSnapshot.Unknown;
        }

        if (pageVersion != 0 && _latestControlUiSnapshotPageVersion != pageVersion)
        {
            return ControlUiProbeSnapshot.Unknown;
        }

        return _latestControlUiSnapshot;
    }

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

    private static ControlUiProbeSnapshot ParseControlUiSnapshot(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.String)
        {
            var nested = root.GetString();
            return string.IsNullOrWhiteSpace(nested)
                ? ControlUiProbeSnapshot.Unknown
                : ParseControlUiSnapshot(nested);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return ControlUiProbeSnapshot.Unknown;
        }

        var kind = GetString(root, "kind");
        if (!string.Equals(kind, ControlUiStatusMessageKind, StringComparison.Ordinal))
        {
            return ControlUiProbeSnapshot.Unknown;
        }

        var phase = ParsePhase(GetString(root, "phase"));
        var summary = GetString(root, "summary");
        var detail = GetString(root, "detail");
        var url = GetString(root, "url");
        var shellDetected = root.TryGetProperty("shellDetected", out var shellProperty) &&
            shellProperty.ValueKind is JsonValueKind.True or JsonValueKind.False &&
            shellProperty.GetBoolean();
        var isBusy = root.TryGetProperty("isBusy", out var busyProperty) &&
            busyProperty.ValueKind is JsonValueKind.True or JsonValueKind.False &&
            busyProperty.GetBoolean();
        var inputFocused = root.TryGetProperty("inputFocused", out var inputFocusedProperty) &&
            inputFocusedProperty.ValueKind is JsonValueKind.True or JsonValueKind.False &&
            inputFocusedProperty.GetBoolean();
        var focusedInputHasText = root.TryGetProperty("focusedInputHasText", out var focusedInputHasTextProperty) &&
            focusedInputHasTextProperty.ValueKind is JsonValueKind.True or JsonValueKind.False &&
            focusedInputHasTextProperty.GetBoolean();
        var isBusyStale = root.TryGetProperty("isBusyStale", out var staleProperty) &&
            staleProperty.ValueKind is JsonValueKind.True or JsonValueKind.False &&
            staleProperty.GetBoolean();
        var busyStaleSeconds = root.TryGetProperty("busyStaleSeconds", out var staleSecondsProperty) &&
            staleSecondsProperty.ValueKind == JsonValueKind.Number &&
            staleSecondsProperty.TryGetInt32(out var parsedBusyStaleSeconds)
                ? parsedBusyStaleSeconds
                : 0;
        var workState = GetString(root, "workState");
        var currentModel = GetString(root, "currentModel");
        var currentModelSource = GetString(root, "currentModelSource");
        var activitySignature = GetString(root, "activitySignature");

        return new ControlUiProbeSnapshot(phase, summary, detail, url, shellDetected, isBusy, inputFocused, workState, currentModel)
        {
            FocusedInputHasText = focusedInputHasText,
            IsBusyStale = isBusyStale,
            BusyStaleSeconds = busyStaleSeconds,
            ActivitySignature = activitySignature,
            ModelSource = currentModelSource,
        };
    }

    private static string GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static ControlUiPhase ParsePhase(string value)
    {
        return value switch
        {
            "loading" => ControlUiPhase.Loading,
            "page_loaded" => ControlUiPhase.PageLoaded,
            "gateway_connecting" => ControlUiPhase.GatewayConnecting,
            "connected" => ControlUiPhase.Connected,
            "auth_required" => ControlUiPhase.AuthRequired,
            "pairing_required" => ControlUiPhase.PairingRequired,
            "origin_rejected" => ControlUiPhase.OriginRejected,
            "gateway_error" => ControlUiPhase.GatewayError,
            "unavailable" => ControlUiPhase.Unavailable,
            _ => ControlUiPhase.Unknown,
        };
    }
}
