// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Xaml.Media;
using OpenClaw.Helpers;
using OpenClaw.Services;

namespace OpenClaw.ViewModels;

public partial class MainViewModel
{
    private void ResetTelemetry()
    {
        HeartbeatSummary = StringResources.HeartbeatWait;
        HeartbeatSummaryBrush = WarningBrush;
        StatusIndicatorBrush = NeutralBrush;
        _lastKnownModelSummaryText = DefaultModelSummary;
        ModelSummaryText = DefaultModelSummary;
        AccessSummaryText = DefaultAccessSummary;
        AccessSummaryBrush = WarningBrush;
        LatencySummaryText = DefaultLatencySummary;
        LatencySummaryBrush = NeutralBrush;
        LatencyTooltipText = LatencyTooltipFormatter.Format(_latencyHistory.CreateSummary());
        WorkStatusText = DefaultWorkStatus;
        WorkStatusBrush = WarningBrush;
        SetRunIndicatorMode(RunIndicatorMode.Wait);
        _lastHeartbeatStatus = null;
        ResetHeartbeatIndicatorsToWarning();
    }

    private void OnLatencyUpdated(ControlUiLatencySnapshot snapshot)
    {
        DispatchUiUpdate(() =>
        {
            if (!IsLatencySnapshotForSelectedEnvironment(snapshot))
            {
                return;
            }

            _latencyHistory.Record(snapshot);
            var latencySummary = _statusPresenter.FormatLatencySummary(
                snapshot,
                CurrentStatusBrushes,
                DefaultLatencySummary);
            LatencySummaryText = latencySummary.Text;
            LatencySummaryBrush = latencySummary.Brush;
            LatencyTooltipText = LatencyTooltipFormatter.Format(
                _latencyHistory.CreateSummary(),
                snapshot.ProxyPoP ?? _lastKnownPoP);
            if (!string.IsNullOrWhiteSpace(snapshot.ProxyPoP))
            {
                _lastKnownPoP = snapshot.ProxyPoP;
            }
        });
    }

    private bool IsLatencySnapshotForSelectedEnvironment(ControlUiLatencySnapshot snapshot)
    {
        var selectedHost = TryGetEnvironmentHost(_selectedEnvironment?.GatewayUrl);
        if (string.IsNullOrWhiteSpace(snapshot.Host))
        {
            return string.IsNullOrWhiteSpace(selectedHost);
        }

        return string.Equals(snapshot.Host, selectedHost, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetEnvironmentHost(string? gatewayUrl)
    {
        if (string.IsNullOrWhiteSpace(gatewayUrl) ||
            !Uri.TryCreate(gatewayUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = string.IsNullOrWhiteSpace(uri.IdnHost)
            ? uri.Host
            : uri.IdnHost;

        return string.IsNullOrWhiteSpace(host)
            ? null
            : host.Trim('[', ']');
    }
}
