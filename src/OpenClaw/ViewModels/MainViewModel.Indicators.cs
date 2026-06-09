// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Xaml.Media;
using OpenClaw.Helpers;
using OpenClaw.Services;

namespace OpenClaw.ViewModels;

public partial class MainViewModel
{
    private void ResetTelemetry()
    {
        ResetHeartbeatProjection();
        StatusIndicatorBrush = NeutralBrush;
        _lastKnownModelSummaryText = DefaultModelSummary;
        ModelSummaryText = DefaultModelSummary;
        AccessSummaryText = DefaultAccessSummary;
        AccessSummaryBrush = WarningBrush;
        ResetLatencyProjection(clearHistory: true);
        WorkStatusText = DefaultWorkStatus;
        WorkStatusBrush = WarningBrush;
        SetRunIndicatorMode(RunIndicatorMode.Wait);
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
            LatencyTooltipText = FormatLatencyTooltip(
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
        if (snapshot.State == ControlUiLatencyState.Unknown)
        {
            return true;
        }

        var selectedProbeKey = TryGetEnvironmentProbeKey(_selectedEnvironment?.GatewayUrl);
        if (!string.IsNullOrWhiteSpace(snapshot.ProbeKey))
        {
            return string.Equals(snapshot.ProbeKey, selectedProbeKey, StringComparison.OrdinalIgnoreCase);
        }

        var selectedHost = TryGetEnvironmentHost(_selectedEnvironment?.GatewayUrl);
        if (string.IsNullOrWhiteSpace(snapshot.Host))
        {
            return string.IsNullOrWhiteSpace(selectedHost);
        }

        return string.Equals(snapshot.Host, selectedHost, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetEnvironmentProbeKey(string? gatewayUrl)
    {
        return ControlUiProbeUriFactory.TryCreateProbeKey(gatewayUrl);
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

    private void ResetLatencyProjection(bool clearHistory = false)
    {
        if (clearHistory)
        {
            _latencyHistory.Clear();
            _lastKnownPoP = null;
        }

        LatencySummaryText = DefaultLatencySummary;
        LatencySummaryBrush = NeutralBrush;
        LatencyTooltipText = FormatLatencyTooltip(_latencyHistory.CreateSummary(), _lastKnownPoP);
    }

    private static string FormatLatencyTooltip(LatencyHistorySummary summary, string? proxyPoP = null)
    {
        if (summary.SampleCount <= 0 ||
            summary.LatestMs is not long latest ||
            summary.MinMs is not long min ||
            summary.AverageMs is not long average ||
            summary.P95Ms is not long p95 ||
            summary.MaxMs is not long max)
        {
            return StringResources.LatencyHistoryNoSamples;
        }

        var lines = new List<string>(8)
        {
            string.Format(StringResources.LatencyHistoryHeaderFormat, summary.SampleCount),
            string.Format(StringResources.LatencyLatestFormat, latest),
            string.Format(StringResources.LatencyMinFormat, min),
            string.Format(StringResources.LatencyAverageFormat, average),
            string.Format(StringResources.LatencyP95Format, p95),
            string.Format(StringResources.LatencyMaxFormat, max),
        };

        if (!string.IsNullOrWhiteSpace(proxyPoP))
        {
            lines.Add(string.Format(StringResources.LatencyPoPFormat, proxyPoP));
        }

        return string.Join('\n', lines);
    }
}
