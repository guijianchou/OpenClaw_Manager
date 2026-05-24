// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Xaml.Media;
using OpenClaw.Helpers;
using OpenClaw.Models;
using OpenClaw.Services;

namespace OpenClaw.ViewModels;

internal readonly record struct StatusBrushes(
    Brush Neutral,
    Brush Success,
    Brush Warning,
    Brush Error);

internal readonly record struct StatusPresentation(string Text, Brush Brush);

internal readonly record struct WorkStatusPresentation(string Text, Brush Brush, RunIndicatorMode Mode);

internal sealed class StatusPresenter
{
    public StatusPresentation FormatHeartbeatSummary(
        HeartbeatProbeStatus status,
        StatusBrushes brushes,
        string defaultHeartbeatSummary)
    {
        return status switch
        {
            HeartbeatProbeStatus.Healthy => new StatusPresentation(StringResources.HeartbeatOk, brushes.Success),
            HeartbeatProbeStatus.Connecting => new StatusPresentation(StringResources.HeartbeatWait, brushes.Warning),
            HeartbeatProbeStatus.SessionBlocked => new StatusPresentation(StringResources.HeartbeatBlocked, brushes.Warning),
            HeartbeatProbeStatus.Failure => new StatusPresentation(StringResources.HeartbeatFailed, brushes.Warning),
            _ => new StatusPresentation(defaultHeartbeatSummary, brushes.Warning),
        };
    }

    public StatusPresentation FormatLatencySummary(
        ControlUiLatencySnapshot snapshot,
        StatusBrushes brushes,
        string defaultLatencySummary)
    {
        if (snapshot.IsSuccess && snapshot.RoundtripTimeMs is long roundtripTimeMs)
        {
            return new StatusPresentation($"{roundtripTimeMs} ms", FormatLatencyBrush(roundtripTimeMs, brushes));
        }

        if (snapshot.State == ControlUiLatencyState.Stale && snapshot.RoundtripTimeMs is long staleRoundtripTimeMs)
        {
            return new StatusPresentation($"{staleRoundtripTimeMs} ms", FormatLatencyBrush(staleRoundtripTimeMs, brushes));
        }

        return new StatusPresentation(defaultLatencySummary, brushes.Neutral);
    }

    public string FormatModelSummary(string model, string defaultModelSummary)
    {
        return string.IsNullOrWhiteSpace(model)
            ? defaultModelSummary
            : model.Trim();
    }

    public StatusPresentation FormatAccessSummary(
        ControlUiProbeSnapshot snapshot,
        StatusBrushes brushes,
        string defaultAccessSummary)
    {
        return snapshot.Phase switch
        {
            ControlUiPhase.Connected => new StatusPresentation("AUTH OK", brushes.Success),
            ControlUiPhase.AuthRequired => new StatusPresentation("AUTH LOGIN", brushes.Warning),
            ControlUiPhase.PairingRequired => new StatusPresentation("AUTH PAIR", brushes.Warning),
            ControlUiPhase.OriginRejected => new StatusPresentation("AUTH ORIGIN", brushes.Warning),
            ControlUiPhase.GatewayConnecting or ControlUiPhase.PageLoaded or ControlUiPhase.Loading =>
                new StatusPresentation("AUTH WAIT", brushes.Warning),
            _ => new StatusPresentation(defaultAccessSummary, brushes.Warning),
        };
    }

    public WorkStatusPresentation FormatWorkStatus(
        ControlUiProbeSnapshot snapshot,
        StatusBrushes brushes,
        string defaultWorkStatus)
    {
        if (snapshot.IsBusy || string.Equals(snapshot.WorkState, "busy", StringComparison.OrdinalIgnoreCase))
        {
            return new WorkStatusPresentation("LIVE", brushes.Success, RunIndicatorMode.Live);
        }

        if (string.Equals(snapshot.WorkState, "idle", StringComparison.OrdinalIgnoreCase) ||
            snapshot.Phase == ControlUiPhase.Connected)
        {
            return new WorkStatusPresentation("IDLE", brushes.Warning, RunIndicatorMode.Idle);
        }

        return new WorkStatusPresentation(defaultWorkStatus, brushes.Warning, RunIndicatorMode.Wait);
    }

    public StatusPresentation FormatShellStatus(
        RecoveryState shellConnectionState,
        string recoveryMessage,
        ConnectionState connectionState,
        StatusBrushes brushes)
    {
        return shellConnectionState switch
        {
            RecoveryState.Reconnecting => new StatusPresentation(StringResources.RecoveryReconnecting, brushes.Warning),
            RecoveryState.Resyncing => new StatusPresentation(StringResources.RecoveryResyncing, brushes.Warning),
            RecoveryState.Refreshing => new StatusPresentation(StringResources.RecoveryRefreshing, brushes.Warning),
            RecoveryState.Degraded when !string.IsNullOrWhiteSpace(recoveryMessage) =>
                new StatusPresentation(recoveryMessage, brushes.Warning),
            RecoveryState.Failed => new StatusPresentation(StringResources.RecoveryFailed, brushes.Error),
            _ => FormatConnectionState(connectionState, brushes),
        };
    }

    public StatusPresentation FormatConnectionState(ConnectionState state, StatusBrushes brushes)
    {
        return state switch
        {
            ConnectionState.Connected => new StatusPresentation(StringResources.StatusConnected, brushes.Success),
            ConnectionState.Loading => new StatusPresentation(StringResources.StatusLoading, brushes.Warning),
            ConnectionState.GatewayConnecting => new StatusPresentation(StringResources.StatusGatewayConnecting, brushes.Warning),
            ConnectionState.Reconnecting => new StatusPresentation(StringResources.StatusReconnecting, brushes.Warning),
            ConnectionState.AuthFailed => new StatusPresentation(StringResources.StatusAuthFailed, brushes.Error),
            ConnectionState.Error => new StatusPresentation(StringResources.StatusError, brushes.Error),
            _ => new StatusPresentation(StringResources.StatusOffline, brushes.Neutral),
        };
    }

    public string FormatRecoveryMessage(RecoveryState state)
    {
        return state switch
        {
            RecoveryState.Connecting => StringResources.RecoveryConnecting,
            RecoveryState.Reconnecting => StringResources.RecoveryReconnecting,
            RecoveryState.Resyncing => StringResources.RecoveryResyncing,
            RecoveryState.Refreshing => StringResources.RecoveryRefreshing,
            RecoveryState.Degraded => StringResources.RecoveryDegraded,
            RecoveryState.Failed => StringResources.RecoveryFailed,
            _ => string.Empty,
        };
    }

    private static Brush FormatLatencyBrush(long roundtripTimeMs, StatusBrushes brushes)
    {
        return roundtripTimeMs switch
        {
            <= 200 => brushes.Success,
            <= 500 => brushes.Warning,
            _ => brushes.Error,
        };
    }
}
