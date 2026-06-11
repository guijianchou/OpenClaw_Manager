// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenClaw.Models;
using OpenClaw.Services;

namespace OpenClaw.Core.Tests;

[TestClass]
public sealed class ShellSessionCoordinatorTests
{
    [TestMethod]
    public async Task SecondRecoveryOperationIsThrottledWhileFirstIsInProgress()
    {
        var webView = new FakeShellWebView();
        var bridge = new FakeShellBridge();
        using var coordinator = new ShellSessionCoordinator();
        await coordinator.AttachAsync(webView, bridge);

        // Hard refresh blocks on the pending inspection.
        var hardRefresh = coordinator.RequestHardRefreshAsync("test_hard_refresh");
        await webView.WaitForInspectionRequestAsync();
        Assert.AreEqual(RecoveryState.Refreshing, coordinator.CurrentRecoveryState);
        Assert.IsTrue(coordinator.IsRecoveryInProgress);

        // A reconnect requested mid-flight must be throttled away.
        await coordinator.RequestReconnectAsync("competing_reconnect");
        Assert.AreEqual(0, webView.ReloadCount);
        Assert.AreEqual(RecoveryState.Refreshing, coordinator.CurrentRecoveryState);

        // Completing the inspection with a healthy session resolves without a reload.
        webView.CompleteInspection(ConnectedSnapshot());
        await hardRefresh;

        Assert.AreEqual(RecoveryState.Ready, coordinator.CurrentRecoveryState);
        Assert.IsFalse(coordinator.IsRecoveryInProgress);
        Assert.AreEqual(0, webView.ReloadCount, "a connected session must not be reloaded");
    }

    [TestMethod]
    public async Task HardRefreshReloadsWhenSessionIsDead()
    {
        var webView = new FakeShellWebView();
        var bridge = new FakeShellBridge();
        using var coordinator = new ShellSessionCoordinator();
        await coordinator.AttachAsync(webView, bridge);

        var hardRefresh = coordinator.RequestHardRefreshAsync("test_hard_refresh");
        await webView.WaitForInspectionRequestAsync();
        webView.CompleteInspection(ControlUiProbeSnapshot.Unavailable("gateway is down"));
        await hardRefresh;

        Assert.AreEqual(1, webView.ReloadCount);
        Assert.AreEqual(RecoveryState.Connecting, coordinator.CurrentRecoveryState);
    }

    [TestMethod]
    public async Task AuthIssueSnapshotSkipsReloadAndMarksAuthIssue()
    {
        var webView = new FakeShellWebView();
        var bridge = new FakeShellBridge();
        using var coordinator = new ShellSessionCoordinator();
        await coordinator.AttachAsync(webView, bridge);

        var hardRefresh = coordinator.RequestHardRefreshAsync("test_hard_refresh");
        await webView.WaitForInspectionRequestAsync();
        webView.CompleteInspection(AuthRequiredSnapshot());
        await hardRefresh;

        Assert.AreEqual(0, webView.ReloadCount, "auth issues must not trigger automatic reloads");
        Assert.AreEqual(RecoveryState.AuthIssue, coordinator.CurrentRecoveryState);
    }

    [TestMethod]
    public async Task DisposeDuringPendingRecoveryCompletesWithoutThrowing()
    {
        var webView = new FakeShellWebView();
        var bridge = new FakeShellBridge();
        var coordinator = new ShellSessionCoordinator();
        await coordinator.AttachAsync(webView, bridge);

        var hardRefresh = coordinator.RequestHardRefreshAsync("test_hard_refresh");
        await webView.WaitForInspectionRequestAsync();

        coordinator.Dispose();
        webView.CompleteInspection(ConnectedSnapshot());
        await hardRefresh;

        Assert.IsFalse(coordinator.IsRecoveryInProgress);
    }

    private static ControlUiProbeSnapshot ConnectedSnapshot() => new(
        ControlUiPhase.Connected,
        "Connected.",
        string.Empty,
        "https://gateway.example.org",
        ShellDetected: true,
        IsBusy: false,
        InputFocused: false,
        WorkState: "idle",
        CurrentModel: "test-model");

    private static ControlUiProbeSnapshot AuthRequiredSnapshot() => new(
        ControlUiPhase.AuthRequired,
        "Authentication required.",
        "Sign in to the gateway.",
        "https://gateway.example.org",
        ShellDetected: true,
        IsBusy: false,
        InputFocused: false,
        WorkState: "idle",
        CurrentModel: string.Empty);

#pragma warning disable CS0067 // Fake implementations do not raise every interface event.
    private sealed class FakeShellWebView : IShellSessionWebView
    {
        private TaskCompletionSource<ControlUiProbeSnapshot> _pendingInspection = CreateCompletion();
        private TaskCompletionSource _inspectionRequested = CreateSignal();
        private int _reloadCount;

        public event Action<ConnectionState>? ConnectionStateChanged;
        public event Action<string>? NavigationErrorOccurred;
        public event Action<string?>? NavigationCompleted;
        public event Action<HeartbeatProbeResult>? HeartbeatObserved;
        public event Action<string>? HeartbeatFailed;
        public event Action<ControlUiProbeSnapshot>? ControlUiSnapshotUpdated;

        public int ReloadCount => Volatile.Read(ref _reloadCount);

        public int TotalControlUiInspectionRequests => 0;

        public int CachedControlUiInspectionRequests => 0;

        public int CoalescedControlUiInspectionRequests => 0;

        public int HeartbeatRecoveryRequests => 0;

        public Task<ControlUiProbeSnapshot> InspectControlUiStateAsync(CancellationToken cancellationToken)
        {
            _inspectionRequested.TrySetResult();
            return _pendingInspection.Task.WaitAsync(cancellationToken);
        }

        public Task<bool> ReloadAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _reloadCount);
            return Task.FromResult(true);
        }

        public Task WaitForInspectionRequestAsync() =>
            _inspectionRequested.Task.WaitAsync(TimeSpan.FromSeconds(30));

        public void CompleteInspection(ControlUiProbeSnapshot snapshot)
        {
            var pending = _pendingInspection;
            _pendingInspection = CreateCompletion();
            _inspectionRequested = CreateSignal();
            pending.TrySetResult(snapshot);
        }

        private static TaskCompletionSource<ControlUiProbeSnapshot> CreateCompletion() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static TaskCompletionSource CreateSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class FakeShellBridge : IShellSessionBridge
    {
        public event Action<SessionReadyEventArgs>? SessionReady;
        public event Action<EventGapEventArgs>? EventGapDetected;

        public Task<bool> RequestSessionRefreshAsync(CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> RequestRecentMessagesAsync(CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> RequestLightweightSyncAsync(CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> NotifyReconnectIntentAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    }
#pragma warning restore CS0067
}
