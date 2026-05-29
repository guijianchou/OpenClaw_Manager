// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.Web.WebView2.Core;

namespace OpenClaw.Services;

internal sealed partial class WebViewStatusInspector : IDisposable
{
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
        ApplyControlUiSnapshot(ControlUiProbeSnapshot.Unknown, _generations.Current, pageVersion: 0, notifySnapshotUpdated: true);
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
}
