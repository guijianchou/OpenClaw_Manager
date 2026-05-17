// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Xaml.Media;
using OpenClaw.Helpers;
using OpenClaw.Services;
using Windows.UI;

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
        RunOnUiThread(() =>
        {
            _latencyHistory.Record(snapshot);
            (LatencySummaryText, LatencySummaryBrush) = FormatLatencySummary(snapshot);
            LatencyTooltipText = LatencyTooltipFormatter.Format(
                _latencyHistory.CreateSummary(),
                snapshot.ProxyPoP ?? _lastKnownPoP);
            if (!string.IsNullOrWhiteSpace(snapshot.ProxyPoP))
            {
                _lastKnownPoP = snapshot.ProxyPoP;
            }
        });
    }

    private static SolidColorBrush CreateBrush(byte red, byte green, byte blue) =>
        new(Color.FromArgb(255, red, green, blue));
}
