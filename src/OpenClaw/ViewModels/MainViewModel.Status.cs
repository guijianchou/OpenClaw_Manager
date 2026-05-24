// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Xaml.Media;
using OpenClaw.Helpers;
using OpenClaw.Models;
using OpenClaw.Services;

namespace OpenClaw.ViewModels;

public partial class MainViewModel
{
    private void OnConnectionStateChanged(ConnectionState state)
    {
        _dispatchToUi(() => ApplyConnectionState(state));
    }

    private void OnNavigationError(string message)
    {
        _dispatchToUi(() => ApplyNavigationError(message));
    }

    private void OnControlUiSnapshotUpdated(ControlUiProbeSnapshot snapshot)
    {
        _dispatchToUi(() => ApplyControlUiSnapshot(snapshot));
    }

    private void OnRecoveryStateChanged(RecoveryState state)
    {
        _dispatchToUi(() => ApplyRecoveryState(state));
    }

    private void OnTelemetryUpdated(RecoveryTelemetrySnapshot snapshot)
    {
        if (!App.Configuration.Settings.Diagnostics.EnableVerboseRecoveryLogging)
        {
            return;
        }

        App.Logger.Info("recovery.telemetry", new
        {
            snapshot.TotalReconnectAttempts,
            snapshot.TotalSoftResyncAttempts,
            snapshot.TotalHardRefreshAttempts,
            snapshot.RecentGapCount,
            snapshot.CurrentRecoveryState
        });
    }

    private void ApplyConnectionState(ConnectionState state)
    {
        ConnectionState = state;
        IsLoading = state is ConnectionState.Loading or ConnectionState.GatewayConnecting;
        ShowRetryButton = state is ConnectionState.Error or ConnectionState.AuthFailed;

        RefreshResourceScheduling();
        UpdateStatusPresentation();
    }

    private void ApplyNavigationError(string message)
    {
        ErrorMessage = message;
        IsErrorVisible = true;
        ErrorOccurred?.Invoke(message);
        UpdateStatusPresentation();
    }

    private void ApplyControlUiSnapshot(ControlUiProbeSnapshot snapshot)
    {
        ApplyModelSummary(snapshot);
        var accessSummary = _statusPresenter.FormatAccessSummary(snapshot, CurrentStatusBrushes, DefaultAccessSummary);
        AccessSummaryText = accessSummary.Text;
        AccessSummaryBrush = accessSummary.Brush;

        ApplyWorkStatus(snapshot);
        ApplySnapshotErrorState(snapshot);
        StartHeartbeatIfReady(snapshot);

        UpdateStatusPresentation();
    }

    private void ApplyModelSummary(ControlUiProbeSnapshot snapshot)
    {
        var modelSummary = _statusPresenter.FormatModelSummary(snapshot.CurrentModel, DefaultModelSummary);
        if (modelSummary != DefaultModelSummary)
        {
            _lastKnownModelSummaryText = modelSummary;
            ModelSummaryText = modelSummary;
            return;
        }

        if (ShouldClearModelSummary(snapshot))
        {
            _lastKnownModelSummaryText = DefaultModelSummary;
            ModelSummaryText = DefaultModelSummary;
            return;
        }

        if (_lastKnownModelSummaryText != DefaultModelSummary)
        {
            ModelSummaryText = _lastKnownModelSummaryText;
        }
    }

    private static bool ShouldClearModelSummary(ControlUiProbeSnapshot snapshot)
    {
        return snapshot.Phase is ControlUiPhase.Loading
            or ControlUiPhase.AuthRequired
            or ControlUiPhase.PairingRequired
            or ControlUiPhase.OriginRejected
            or ControlUiPhase.GatewayError
            or ControlUiPhase.Unavailable
            or ControlUiPhase.Unknown;
    }

    private void ApplyRecoveryState(RecoveryState state)
    {
        ShellConnectionState = state;
        IsRecovering = state is RecoveryState.Reconnecting or RecoveryState.Resyncing or RecoveryState.Refreshing;
        RecoveryMessage = _statusPresenter.FormatRecoveryMessage(state);
        UpdateStatusPresentation();
    }

    private void ApplyWorkStatus(ControlUiProbeSnapshot snapshot)
    {
        var presentation = _statusPresenter.FormatWorkStatus(snapshot, CurrentStatusBrushes, DefaultWorkStatus);
        WorkStatusText = presentation.Text;
        WorkStatusBrush = presentation.Brush;
        SetRunIndicatorMode(presentation.Mode);
    }

    private void ApplySnapshotErrorState(ControlUiProbeSnapshot snapshot)
    {
        if (snapshot.IsIssue && ConnectionState is ConnectionState.Error or ConnectionState.AuthFailed or ConnectionState.Reconnecting)
        {
            ErrorMessage = snapshot.DetailOrSummary;
        }
        else if (ConnectionState is not ConnectionState.Error and not ConnectionState.AuthFailed)
        {
            IsErrorVisible = false;
        }
    }

    private void StartHeartbeatIfReady(ControlUiProbeSnapshot snapshot)
    {
        RefreshResourceScheduling();
    }

}
