using System.Net;
using System.Reflection;
using OpenClaw.Helpers;
using OpenClaw.Models;
using OpenClaw.Services;

internal static partial class Tests
{
    public static async Task ResetCancelsInFlightReconnectAsync()
    {
        var webView = new FakeShellSessionWebView();
        var bridge = new FakeShellSessionBridge();
        var coordinator = CreateCoordinator(webView, bridge, new RecoveryPolicyOptions
        {
            ReconnectDelayMs = 5_000,
            MaxReconnectDelayMs = 5_000,
            ReconnectBackoffMultiplier = 1
        });

        var recoveryTask = coordinator.RequestReconnectAsync("test-reset-cancel");
        await Task.Delay(50);

        coordinator.Reset();
        await recoveryTask;

        Assert.Equal(RecoveryState.Connecting, coordinator.CurrentRecoveryState, "Reset should restore connecting state.");
        Assert.False(coordinator.IsRecoveryInProgress, "Recovery should not remain in progress after reset.");
        Assert.Equal(0, webView.ReloadCount, "Reconnect should be cancelled before reload.");

        var telemetry = coordinator.GetTelemetrySnapshot();
        Assert.Null(telemetry.LastRecoveryReason, "Reset should clear the last recovery reason.");
        Assert.Null(telemetry.LastRecoveryStartedAt, "Reset should clear the last recovery timestamp.");
    }

    public static async Task ReconnectFallsBackToReloadWhenInPageRecoveryDoesNotReviveSessionAsync()
    {
        var webView = new FakeShellSessionWebView();
        webView.EnqueueSnapshot(ControlUiProbeSnapshot.Unavailable("post-in-page reconnect still unavailable"));
        webView.EnqueueSnapshot(ControlUiProbeSnapshot.Unavailable("pre-reload still unavailable"));

        var bridge = new FakeShellSessionBridge
        {
            ReconnectIntentResult = true
        };

        var coordinator = CreateCoordinator(webView, bridge, new RecoveryPolicyOptions
        {
            ReconnectDelayMs = 1,
            MaxReconnectDelayMs = 1,
            ReconnectBackoffMultiplier = 1,
            HardRefreshCooldownSeconds = 0
        });

        await coordinator.RequestReconnectAsync("fallback");

        var telemetry = coordinator.GetTelemetrySnapshot();
        Assert.Equal(1, bridge.NotifyReconnectIntentCalls, "Reconnect should notify the hosted UI before falling back.");
        Assert.Equal(1, bridge.RequestSessionRefreshCalls, "Reconnect should still request an in-page refresh before reload.");
        Assert.Equal(2, webView.InspectCount, "Reconnect fallback should inspect both post-in-page and pre-reload snapshots.");
        Assert.Equal(1, webView.ReloadCount, "Unavailable reconnect fallback should reload the page.");
        Assert.Equal(RecoveryState.Connecting, coordinator.CurrentRecoveryState, "Reload fallback should leave the coordinator connecting.");
        Assert.Equal(1, telemetry.TotalReconnectAttempts, "Reconnect fallback should count the reconnect attempt.");
        Assert.Equal("fallback", telemetry.LastRecoveryReason, "Reconnect fallback should preserve the triggering reason.");
    }

    public static async Task InputFocusedReloadDeferDoesNotRecordSuccessAsync()
    {
        var webView = new FakeShellSessionWebView();
        webView.EnqueueSnapshot(new ControlUiProbeSnapshot(
            ControlUiPhase.GatewayError,
            "Gateway error",
            "stream unavailable",
            "https://gateway.example",
            ShellDetected: true,
            IsBusy: false,
            InputFocused: true,
            WorkState: "unknown",
            CurrentModel: "gpt-test")
        {
            FocusedInputHasText = true,
        });
        var coordinator = CreateCoordinator(webView, new FakeShellSessionBridge());

        await coordinator.RequestHardRefreshAsync("input-focused");

        var telemetry = coordinator.GetTelemetrySnapshot();
        Assert.Equal(RecoveryState.Degraded, telemetry.CurrentRecoveryState, "Focused input with unsent text should defer reload and mark recovery degraded.");
        Assert.Null(telemetry.LastSuccessfulRecoveryAt, "Deferred reload should not be recorded as a successful recovery.");
        Assert.Equal(0, webView.ReloadCount, "Deferred reload should not reload the page.");
    }

    public static async Task InputFocusedEmptyEditorDoesNotBlockHardRefreshAsync()
    {
        var webView = new FakeShellSessionWebView();
        webView.EnqueueSnapshot(new ControlUiProbeSnapshot(
            ControlUiPhase.GatewayError,
            "Gateway error",
            "stream unavailable",
            "https://gateway.example",
            ShellDetected: true,
            IsBusy: false,
            InputFocused: true,
            WorkState: "unknown",
            CurrentModel: "gpt-test")
        {
            FocusedInputHasText = false,
        });
        var coordinator = CreateCoordinator(webView, new FakeShellSessionBridge());

        await coordinator.RequestHardRefreshAsync("input-focused-empty");

        Assert.Equal(1, webView.ReloadCount, "Focused but empty chat input should not block recovery reload.");
        Assert.Equal(RecoveryState.Connecting, coordinator.CurrentRecoveryState, "Hard refresh should leave the coordinator reconnecting.");
    }

    public static async Task StaleBusySnapshotRequestsSoftResyncAsync()
    {
        var webView = new FakeShellSessionWebView();
        var bridge = new FakeShellSessionBridge
        {
            LightweightSyncResult = true,
            RecentMessagesResult = true,
        };
        var coordinator = CreateCoordinator(webView, bridge);

        webView.EnqueueSnapshot(new ControlUiProbeSnapshot(
            ControlUiPhase.Connected,
            "Connected",
            string.Empty,
            "https://gateway.example",
            ShellDetected: true,
            IsBusy: false,
            InputFocused: false,
            WorkState: "idle",
            CurrentModel: "gpt-test"));
        webView.RaiseControlUiSnapshotUpdated(new ControlUiProbeSnapshot(
            ControlUiPhase.Connected,
            "Connected",
            string.Empty,
            "https://gateway.example",
            ShellDetected: true,
            IsBusy: true,
            InputFocused: false,
            WorkState: "busy",
            CurrentModel: "gpt-test")
        {
            IsBusyStale = true,
            BusyStaleSeconds = 35,
            ActivitySignature = "run-123:assistant:42",
        });

        await WaitUntilAsync(() => bridge.RequestLightweightSyncCalls == 1, TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => coordinator.CurrentRecoveryState == RecoveryState.Ready, TimeSpan.FromSeconds(3));

        Assert.Equal(1, bridge.RequestRecentMessagesCalls, "Stale busy recovery should also fetch recent messages.");
        Assert.Equal(0, webView.ReloadCount, "Stale busy recovery should attempt soft resync before reload.");
        Assert.Equal(RecoveryState.Ready, coordinator.CurrentRecoveryState, "Successful stale busy soft resync should return the coordinator to ready.");
    }

    public static async Task StaleBusySnapshotEscalatesToHardRefreshAfterSoftResyncBudgetAsync()
    {
        var webView = new FakeShellSessionWebView();
        var bridge = new FakeShellSessionBridge();
        var coordinator = CreateCoordinator(webView, bridge, new RecoveryPolicyOptions
        {
            MaxSoftResyncAttempts = 0,
            HardRefreshCooldownSeconds = 0,
        });
        var staleSnapshot = new ControlUiProbeSnapshot(
            ControlUiPhase.Connected,
            "Connected",
            string.Empty,
            "https://gateway.example",
            ShellDetected: true,
            IsBusy: true,
            InputFocused: false,
            WorkState: "busy",
            CurrentModel: "gpt-test")
        {
            IsBusyStale = true,
            BusyStaleSeconds = 45,
            ActivitySignature = "run-456:assistant:42",
        };

        webView.EnqueueSnapshot(staleSnapshot);
        webView.RaiseControlUiSnapshotUpdated(staleSnapshot);

        await WaitUntilAsync(() => webView.ReloadCount == 1, TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => coordinator.CurrentRecoveryState == RecoveryState.Connecting, TimeSpan.FromSeconds(3));

        Assert.Equal(0, bridge.RequestLightweightSyncCalls, "Exhausted stale busy recovery should skip additional soft resync attempts.");
        Assert.Equal(0, bridge.RequestRecentMessagesCalls, "Exhausted stale busy recovery should reload instead of repeatedly fetching recent messages.");
    }

    public static async Task SoftResyncUnsupportedMarksRecoveryDegradedAsync()
    {
        var webView = new FakeShellSessionWebView();
        var bridge = new FakeShellSessionBridge();
        var coordinator = CreateCoordinator(webView, bridge);

        await coordinator.RequestSoftResyncAsync("unsupported");

        var telemetry = coordinator.GetTelemetrySnapshot();
        Assert.Equal(1, bridge.RequestLightweightSyncCalls, "Soft resync should try lightweight sync first.");
        Assert.Equal(1, bridge.RequestRecentMessagesCalls, "Soft resync should also try recent messages before giving up.");
        Assert.Equal(RecoveryState.Degraded, coordinator.CurrentRecoveryState, "Unsupported soft resync should degrade recovery state.");
        Assert.Equal(0, webView.ReloadCount, "Unsupported soft resync should not reload the page.");
        Assert.Equal(1, telemetry.TotalSoftResyncAttempts, "Unsupported soft resync should count the attempted recovery.");
        Assert.Equal("unsupported", telemetry.LastRecoveryReason, "Unsupported soft resync should keep the triggering reason.");
        Assert.Null(telemetry.LastSuccessfulRecoveryAt, "Unsupported soft resync should not record success.");
    }

    public static async Task EventGapRequestsSoftResyncAsync()
    {
        var webView = new FakeShellSessionWebView();
        webView.EnqueueSnapshot(ControlUiProbeSnapshot.PageLoaded("https://gateway.example"));
        webView.EnqueueSnapshot(ControlUiProbeSnapshot.PageLoaded("https://gateway.example"));

        var bridge = new FakeShellSessionBridge
        {
            LightweightSyncResult = true,
            RecentMessagesResult = true,
        };

        var coordinator = CreateCoordinator(webView, bridge, new RecoveryPolicyOptions
        {
            ReconnectDelayMs = 1,
            MaxReconnectDelayMs = 1,
            ReconnectBackoffMultiplier = 1
        });

        bridge.RaiseEventGap(new EventGapEventArgs(10, 12, "v1", "v2", DateTimeOffset.UtcNow.ToString("O")));
        await Task.Delay(1_800);

        Assert.Equal(1, bridge.RequestLightweightSyncCalls, "Alive session gap should attempt lightweight sync first.");
        Assert.Equal(1, bridge.RequestRecentMessagesCalls, "Alive session gap should also request recent messages.");
        Assert.Equal(0, webView.ReloadCount, "Soft resync path should not reload the page.");
        Assert.Equal(RecoveryState.Ready, coordinator.CurrentRecoveryState, "Successful soft resync should return coordinator to ready.");
    }

    public static async Task EventGapEscalatesToHardRefreshAsync()
    {
        var webView = new FakeShellSessionWebView();
        webView.EnqueueSnapshot(ControlUiProbeSnapshot.Unavailable("gap-detected"));
        webView.EnqueueSnapshot(ControlUiProbeSnapshot.Unavailable("hard-refresh"));

        var bridge = new FakeShellSessionBridge();
        var coordinator = CreateCoordinator(webView, bridge, new RecoveryPolicyOptions
        {
            MaxReconnectAttempts = 0,
            MaxSoftResyncAttempts = 0,
            ReconnectDelayMs = 1,
            MaxReconnectDelayMs = 1,
            ReconnectBackoffMultiplier = 1
        });

        bridge.RaiseEventGap(new EventGapEventArgs(3, 9, "v1", "v2", DateTimeOffset.UtcNow.ToString("O")));
        await Task.Delay(1_300);

        Assert.Equal(1, webView.ReloadCount, "Exhausted gap recovery should escalate to hard refresh reload.");
        Assert.Equal(RecoveryState.Connecting, coordinator.CurrentRecoveryState, "Hard refresh should leave the coordinator reconnecting.");
    }

    public static async Task AuthIssueShortCircuitsRecoveryAsync()
    {
        var webView = new FakeShellSessionWebView();
        webView.EnqueueSnapshot(CreateAuthRequiredSnapshot());

        var bridge = new FakeShellSessionBridge();
        var coordinator = CreateCoordinator(webView, bridge);

        bridge.RaiseEventGap(new EventGapEventArgs(1, 5, "v1", "v2", DateTimeOffset.UtcNow.ToString("O")));
        await Task.Delay(150);

        Assert.Equal(RecoveryState.AuthIssue, coordinator.CurrentRecoveryState, "Auth issues should short-circuit recovery.");
        Assert.Equal(0, bridge.RequestLightweightSyncCalls, "Auth short-circuit should avoid sync requests.");
        Assert.Equal(0, bridge.NotifyReconnectIntentCalls, "Auth short-circuit should avoid reconnect requests.");
        Assert.Equal(0, webView.ReloadCount, "Auth short-circuit should avoid reloads.");
    }

    public static async Task HardRefreshShortCircuitsOnAuthIssueAsync()
    {
        var webView = new FakeShellSessionWebView();
        webView.EnqueueSnapshot(CreateAuthRequiredSnapshot());

        var coordinator = CreateCoordinator(webView, new FakeShellSessionBridge());

        await coordinator.RequestHardRefreshAsync("auth-refresh");

        var telemetry = coordinator.GetTelemetrySnapshot();
        Assert.Equal(RecoveryState.AuthIssue, coordinator.CurrentRecoveryState, "Hard refresh should stop when auth is required.");
        Assert.Equal(0, webView.ReloadCount, "Auth-gated hard refresh should not reload the page.");
        Assert.Equal(1, telemetry.TotalHardRefreshAttempts, "Auth short-circuit should still count the attempted hard refresh.");
        Assert.Equal("auth-refresh", telemetry.LastRecoveryReason, "Auth short-circuit should preserve the triggering reason.");
        Assert.Null(telemetry.LastSuccessfulRecoveryAt, "Auth short-circuit should not record a successful recovery.");
    }

    public static async Task BackgroundResumeReconnectsWhenSessionIsGoneAsync()
    {
        var webView = new FakeShellSessionWebView();
        webView.EnqueueSnapshot(ControlUiProbeSnapshot.Unavailable("background-resume"));
        webView.EnqueueSnapshot(ControlUiProbeSnapshot.Unavailable("pre-reload"));

        var coordinator = CreateCoordinator(webView, new FakeShellSessionBridge(), new RecoveryPolicyOptions
        {
            EnableBackgroundResume = true,
            BackgroundResumeThresholdSeconds = 0,
            ReconnectDelayMs = 1,
            MaxReconnectDelayMs = 1,
            ReconnectBackoffMultiplier = 1,
            HardRefreshCooldownSeconds = 0
        });

        coordinator.OnHostHidden();
        await Task.Delay(20);
        await coordinator.OnHostVisibleAsync();
        await Task.Delay(100);

        Assert.Equal(1, webView.ReloadCount, "Background resume should trigger reconnect reload when the session is gone.");
        Assert.Equal(RecoveryState.Connecting, coordinator.CurrentRecoveryState, "Reconnect path should leave the coordinator connecting.");
        Assert.Equal("Background resume threshold exceeded", coordinator.GetTelemetrySnapshot().LastRecoveryReason, "Reconnect reason should be recorded.");
    }

    public static async Task BackgroundResumeSkipsReconnectWhenSessionAliveAsync()
    {
        var webView = new FakeShellSessionWebView();
        webView.EnqueueSnapshot(ControlUiProbeSnapshot.PageLoaded("https://gateway.example"));

        var coordinator = CreateCoordinator(webView, new FakeShellSessionBridge(), new RecoveryPolicyOptions
        {
            EnableBackgroundResume = true,
            BackgroundResumeThresholdSeconds = 0,
            ReconnectDelayMs = 1,
            MaxReconnectDelayMs = 1,
            ReconnectBackoffMultiplier = 1,
            HardRefreshCooldownSeconds = 0
        });

        coordinator.OnHostHidden();
        await Task.Delay(20);
        await coordinator.OnHostVisibleAsync();

        Assert.Equal(0, webView.ReloadCount, "Healthy background resume should not trigger reconnect reload.");
        Assert.Equal(RecoveryState.Connecting, coordinator.CurrentRecoveryState, "Skipped background resume should leave the existing state untouched.");
        Assert.Null(coordinator.GetTelemetrySnapshot().LastRecoveryReason, "Skipped background resume should not record a new recovery reason.");
    }

    public static async Task HardRefreshRespectsCooldownAsync()
    {
        var webView = new FakeShellSessionWebView();
        webView.EnqueueSnapshot(ControlUiProbeSnapshot.Unavailable("hard-refresh"));

        var coordinator = CreateCoordinator(webView, new FakeShellSessionBridge(), new RecoveryPolicyOptions
        {
            ReconnectDelayMs = 1,
            MaxReconnectDelayMs = 1,
            ReconnectBackoffMultiplier = 1,
            HardRefreshCooldownSeconds = 3600
        });

        await coordinator.RequestHardRefreshAsync("first-refresh");
        await coordinator.RequestHardRefreshAsync("second-refresh");

        var telemetry = coordinator.GetTelemetrySnapshot();
        Assert.Equal(1, webView.ReloadCount, "Hard refresh should reload only once within the cooldown window.");
        Assert.Equal(1, telemetry.TotalHardRefreshAttempts, "Cooldown throttling should prevent a second hard refresh attempt from being counted.");
        Assert.Equal("first-refresh", telemetry.LastRecoveryReason, "Throttled hard refresh should not overwrite the last successful start reason.");
    }

    public static async Task ResetClearsRecoveryAttemptTotalsAsync()
    {
        var webView = new FakeShellSessionWebView();
        webView.EnqueueSnapshot(ControlUiProbeSnapshot.Unavailable("still unavailable"));
        webView.EnqueueSnapshot(ControlUiProbeSnapshot.Unavailable("still unavailable"));
        var coordinator = CreateCoordinator(webView, new FakeShellSessionBridge());

        await coordinator.RequestReconnectAsync("before-reset");
        Assert.Equal(1, coordinator.GetTelemetrySnapshot().TotalReconnectAttempts, "Sanity check should record the reconnect attempt before reset.");

        coordinator.Reset();
        var telemetry = coordinator.GetTelemetrySnapshot();

        Assert.Equal(0, telemetry.TotalReconnectAttempts, "Reset should clear total reconnect attempts.");
        Assert.Equal(0, telemetry.TotalSoftResyncAttempts, "Reset should clear total soft resync attempts.");
        Assert.Equal(0, telemetry.TotalHardRefreshAttempts, "Reset should clear total hard refresh attempts.");
    }
}
