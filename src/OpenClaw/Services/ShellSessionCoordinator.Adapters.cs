// Copyright (c) Lanstack @openclaw. All rights reserved.

using OpenClaw.Models;

namespace OpenClaw.Services;

public static class ShellSessionCoordinatorAdapters
{
    public static Task AttachAsync(
        this ShellSessionCoordinator coordinator,
        WebViewService webViewService,
        HostedUiBridge bridge,
        RecoveryPolicyOptions recoveryOptions,
        HeartbeatOptions heartbeatOptions,
        IAppLogger logger,
        Func<Action, bool> dispatchToUi)
    {
        var dispatcher = new UiTaskDispatcher(dispatchToUi);
        return coordinator.AttachAsync(
            new ShellSessionWebViewAdapter(webViewService, dispatcher),
            new ShellSessionBridgeAdapter(bridge, dispatcher),
            recoveryOptions,
            heartbeatOptions,
            logger);
    }
}

internal sealed class ShellSessionWebViewAdapter : IShellSessionWebView
{
    private readonly WebViewService _inner;
    private readonly UiTaskDispatcher _dispatcher;

    public ShellSessionWebViewAdapter(WebViewService inner, UiTaskDispatcher dispatcher)
    {
        _inner = inner;
        _dispatcher = dispatcher;
    }

    public event Action<ConnectionState>? ConnectionStateChanged
    {
        add => _inner.ConnectionStateChanged += value;
        remove => _inner.ConnectionStateChanged -= value;
    }

    public event Action<string>? NavigationErrorOccurred
    {
        add => _inner.NavigationErrorOccurred += value;
        remove => _inner.NavigationErrorOccurred -= value;
    }

    public event Action<string?>? NavigationCompleted
    {
        add => _inner.NavigationCompleted += value;
        remove => _inner.NavigationCompleted -= value;
    }

    public event Action<HeartbeatProbeResult>? HeartbeatObserved
    {
        add => _inner.HeartbeatObserved += value;
        remove => _inner.HeartbeatObserved -= value;
    }

    public event Action<string>? HeartbeatFailed
    {
        add => _inner.HeartbeatFailed += value;
        remove => _inner.HeartbeatFailed -= value;
    }

    public event Action<ControlUiProbeSnapshot>? ControlUiSnapshotUpdated
    {
        add => _inner.ControlUiSnapshotUpdated += value;
        remove => _inner.ControlUiSnapshotUpdated -= value;
    }

    public Task<ControlUiProbeSnapshot> InspectControlUiStateAsync(CancellationToken cancellationToken) =>
        _inner.InspectControlUiStateAsync(cancellationToken);

    public Task<bool> ReloadAsync(CancellationToken cancellationToken) =>
        _dispatcher.RunAsync(_inner.Reload, cancellationToken);

    public int TotalControlUiInspectionRequests => _inner.TotalControlUiInspectionRequests;

    public int CachedControlUiInspectionRequests => _inner.CachedControlUiInspectionRequests;

    public int CoalescedControlUiInspectionRequests => _inner.CoalescedControlUiInspectionRequests;

    public int HeartbeatRecoveryRequests => _inner.HeartbeatRecoveryRequests;
}

internal sealed class ShellSessionBridgeAdapter : IShellSessionBridge
{
    private readonly HostedUiBridge _inner;
    private readonly UiTaskDispatcher _dispatcher;

    public ShellSessionBridgeAdapter(HostedUiBridge inner, UiTaskDispatcher dispatcher)
    {
        _inner = inner;
        _dispatcher = dispatcher;
    }

    public event Action<SessionReadyEventArgs>? SessionReady
    {
        add => _inner.SessionReady += value;
        remove => _inner.SessionReady -= value;
    }

    public event Action<EventGapEventArgs>? EventGapDetected
    {
        add => _inner.EventGapDetected += value;
        remove => _inner.EventGapDetected -= value;
    }

    public Task<bool> RequestSessionRefreshAsync(CancellationToken cancellationToken) =>
        _dispatcher.RunAsync(() => _inner.RequestSessionRefreshAsync(cancellationToken), cancellationToken);

    public Task<bool> RequestRecentMessagesAsync(CancellationToken cancellationToken) =>
        _dispatcher.RunAsync(() => _inner.RequestRecentMessagesAsync(cancellationToken), cancellationToken);

    public Task<bool> RequestLightweightSyncAsync(CancellationToken cancellationToken) =>
        _dispatcher.RunAsync(() => _inner.RequestLightweightSyncAsync(cancellationToken), cancellationToken);

    public Task<bool> NotifyReconnectIntentAsync(CancellationToken cancellationToken) =>
        _dispatcher.RunAsync(() => _inner.NotifyReconnectIntentAsync(cancellationToken), cancellationToken);
}
