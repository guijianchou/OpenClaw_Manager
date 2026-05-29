// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

public partial class WebViewService
{
    private ControlUiProbeSnapshot _lastPublishedControlUiSnapshot = ControlUiProbeSnapshot.Unknown;
    private string? _lastReportedIssueKey;

    /// <summary>
    /// Raised when the hosted Control UI reports an updated snapshot.
    /// </summary>
    public event Action<ControlUiProbeSnapshot>? ControlUiSnapshotUpdated;

    /// <summary>
    /// Gets the latest control UI probe snapshot observed from the hosted page.
    /// </summary>
    public ControlUiProbeSnapshot LatestControlUiSnapshot => _statusInspector.LatestSnapshot;

    public int TotalControlUiInspectionRequests => _statusInspector.TotalRequests;

    public int CachedControlUiInspectionRequests => _statusInspector.CachedRequests;

    public int CoalescedControlUiInspectionRequests => _statusInspector.CoalescedRequests;

    /// <summary>
    /// Attempts to inspect the hosted Control UI state via the injected page bridge.
    /// </summary>
    public Task<ControlUiProbeSnapshot> InspectControlUiStateAsync(
        CancellationToken cancellationToken = default,
        bool publishSnapshot = true)
    {
        return _uiDispatcher.RunAsync(() => _statusInspector.InspectAsync(cancellationToken, publishSnapshot), cancellationToken);
    }

    private void InvalidateControlUiInspectionCache()
    {
        _statusInspector.InvalidateCache();
    }

    private void StartStatusProbeLoop()
    {
        _statusInspector.StartProbeLoop();
    }

    private void CancelStatusProbeLoop()
    {
        _statusInspector.CancelProbeLoop();
    }

    private void ApplyControlUiSnapshot(ControlUiProbeSnapshot snapshot, bool raiseIssueEvent)
    {
        var notifySnapshotUpdated = !EqualityComparer<ControlUiProbeSnapshot>.Default.Equals(
            _lastPublishedControlUiSnapshot,
            snapshot);
        _lastPublishedControlUiSnapshot = snapshot;

        switch (snapshot.Phase)
        {
            case ControlUiPhase.Loading:
                SetState(ConnectionState.Loading);
                break;
            case ControlUiPhase.PageLoaded:
            case ControlUiPhase.GatewayConnecting:
                SetState(ConnectionState.GatewayConnecting);
                break;
            case ControlUiPhase.Connected:
                _lastReportedIssueKey = null;
                SetState(ConnectionState.Connected);
                break;
            case ControlUiPhase.AuthRequired:
                SetState(ConnectionState.AuthFailed);
                break;
            case ControlUiPhase.PairingRequired:
            case ControlUiPhase.OriginRejected:
            case ControlUiPhase.GatewayError:
                SetState(ConnectionState.Error);
                break;
            case ControlUiPhase.Unavailable:
                SetState(ConnectionState.Reconnecting);
                break;
            case ControlUiPhase.Unknown:
            default:
                break;
        }

        if (notifySnapshotUpdated)
        {
            ControlUiSnapshotUpdated?.Invoke(snapshot);
        }

        if (!raiseIssueEvent || !snapshot.IsIssue)
        {
            return;
        }

        if (string.Equals(snapshot.IssueKey, _lastReportedIssueKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastReportedIssueKey = snapshot.IssueKey;
        NavigationErrorOccurred?.Invoke(snapshot.DetailOrSummary);
    }
}
