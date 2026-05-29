// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

public interface IShellSessionWebView
{
    event Action<ConnectionState>? ConnectionStateChanged;
    event Action<string>? NavigationErrorOccurred;
    event Action<string?>? NavigationCompleted;
    event Action<HeartbeatProbeResult>? HeartbeatObserved;
    event Action<string>? HeartbeatFailed;
    event Action<ControlUiProbeSnapshot>? ControlUiSnapshotUpdated;

    Task<ControlUiProbeSnapshot> InspectControlUiStateAsync(CancellationToken cancellationToken);
    Task<bool> ReloadAsync(CancellationToken cancellationToken);
    int TotalControlUiInspectionRequests { get; }
    int CachedControlUiInspectionRequests { get; }
    int CoalescedControlUiInspectionRequests { get; }
    int HeartbeatRecoveryRequests { get; }
}

public interface IShellSessionBridge
{
    event Action<SessionReadyEventArgs>? SessionReady;
    event Action<EventGapEventArgs>? EventGapDetected;

    Task<bool> RequestSessionRefreshAsync(CancellationToken cancellationToken);
    Task<bool> RequestRecentMessagesAsync(CancellationToken cancellationToken);
    Task<bool> RequestLightweightSyncAsync(CancellationToken cancellationToken);
    Task<bool> NotifyReconnectIntentAsync(CancellationToken cancellationToken);
}
