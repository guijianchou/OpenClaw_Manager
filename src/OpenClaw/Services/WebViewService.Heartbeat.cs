// Copyright (c) Lanstack @openclaw. All rights reserved.

using OpenClaw.Models;

namespace OpenClaw.Services;

public partial class WebViewService
{
    private const int DefaultHeartbeatFailureThreshold = 3;
    private const int DefaultHeartbeatConnectingThreshold = 3;

    private readonly HeartbeatRuntime _heartbeatRuntime;
    private int _heartbeatFailureCount;
    private int _heartbeatConnectingCount;
    private string? _lastHeartbeatObservationKey;
    private string? _heartbeatGatewayUrl;
    private int _heartbeatIntervalSeconds;
    private int _heartbeatFailureThreshold = DefaultHeartbeatFailureThreshold;
    private int _heartbeatConnectingThreshold = DefaultHeartbeatConnectingThreshold;
    private static readonly TimeSpan DefaultHeartbeatReloadCooldown = TimeSpan.FromSeconds(75);
    private int _heartbeatHardRefreshCooldownSeconds = (int)DefaultHeartbeatReloadCooldown.TotalSeconds;
    private static readonly HttpClient HeartbeatHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private DateTimeOffset _lastHeartbeatReloadAt = DateTimeOffset.MinValue;
    private string? _lastStartHeartbeatKey;
    private int _heartbeatRecoveryRequests;

    /// <summary>
    /// Raised when the heartbeat decides the hosted Control UI should be refreshed.
    /// </summary>
    public event Action<string>? HeartbeatFailed;

    /// <summary>
    /// Raised when the heartbeat records a health observation for the hosted Control UI.
    /// </summary>
    public event Action<HeartbeatProbeResult>? HeartbeatObserved;

    public int HeartbeatRecoveryRequests => Volatile.Read(ref _heartbeatRecoveryRequests);

    /// <summary>
    /// Starts the periodic heartbeat probe against the given gateway URL.
    /// If the interval is 0 or negative, the heartbeat is disabled.
    /// </summary>
    public void StartHeartbeat(
        string gatewayUrl,
        HeartbeatOptions heartbeatSettings,
        RecoveryPolicyOptions recoveryPolicyOptions)
    {
        if (!heartbeatSettings.EnableHeartbeat)
        {
            StopHeartbeat();
            _logger.Info("Heartbeat disabled in settings.");
            return;
        }

        var intervalSeconds = heartbeatSettings.IntervalSeconds;
        if (intervalSeconds <= 0 || string.IsNullOrEmpty(gatewayUrl))
        {
            StopHeartbeat();
            _logger.Info("Heartbeat disabled (interval=0 or no URL).");
            return;
        }

        var heartbeatStartKey = $"{gatewayUrl}|{intervalSeconds}|{Math.Max(1, heartbeatSettings.FailureThreshold)}|{Math.Max(1, heartbeatSettings.ConnectingThreshold)}|{Math.Max(0, recoveryPolicyOptions.HardRefreshCooldownSeconds)}";
        if (_heartbeatRuntime.IsSameRun(heartbeatStartKey))
        {
            return;
        }

        StopHeartbeat();

        _heartbeatFailureCount = 0;
        _heartbeatConnectingCount = 0;
        _lastHeartbeatObservationKey = null;
        _heartbeatGatewayUrl = gatewayUrl;
        _heartbeatIntervalSeconds = intervalSeconds;
        _heartbeatFailureThreshold = Math.Max(1, heartbeatSettings.FailureThreshold);
        _heartbeatConnectingThreshold = Math.Max(1, heartbeatSettings.ConnectingThreshold);
        _heartbeatHardRefreshCooldownSeconds = Math.Max(0, recoveryPolicyOptions.HardRefreshCooldownSeconds);

        if (!string.Equals(_lastStartHeartbeatKey, heartbeatStartKey, StringComparison.Ordinal))
        {
            _lastStartHeartbeatKey = heartbeatStartKey;
            _logger.Info($"Heartbeat started: interval={intervalSeconds}s, failureThreshold={_heartbeatFailureThreshold}, connectingThreshold={_heartbeatConnectingThreshold}, url={gatewayUrl}");
        }
        _heartbeatRuntime.Start(
            heartbeatStartKey,
            token => RunSessionAwareHeartbeatLoopAsync(gatewayUrl, TimeSpan.FromSeconds(intervalSeconds), token));
    }

    /// <summary>
    /// Stops the periodic heartbeat probe.
    /// </summary>
    public void StopHeartbeat()
    {
        _heartbeatRuntime.Stop();
        _heartbeatFailureCount = 0;
        _heartbeatConnectingCount = 0;
        _lastHeartbeatObservationKey = null;
        _heartbeatGatewayUrl = null;
        _heartbeatIntervalSeconds = 0;
        _heartbeatFailureThreshold = DefaultHeartbeatFailureThreshold;
        _heartbeatConnectingThreshold = DefaultHeartbeatConnectingThreshold;
        _heartbeatHardRefreshCooldownSeconds = (int)DefaultHeartbeatReloadCooldown.TotalSeconds;
        _lastStartHeartbeatKey = null;
    }

    private async Task RunSessionAwareHeartbeatLoopAsync(string gatewayUrl, TimeSpan interval, CancellationToken token)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                var probe = await ProbeGatewayHealthAsync(gatewayUrl, token);
                LogHeartbeatObservation(probe);

                if (probe.Status == HeartbeatProbeStatus.Healthy)
                {
                    if (_heartbeatFailureCount > 0)
                    {
                        _logger.Info($"Heartbeat recovered after {_heartbeatFailureCount} failure(s).");
                    }

                    _heartbeatFailureCount = 0;
                    _heartbeatConnectingCount = 0;
                    continue;
                }

                if (probe.Status == HeartbeatProbeStatus.SessionBlocked)
                {
                    if (_heartbeatFailureCount > 0)
                    {
                        _logger.Info("Heartbeat failure counter reset because the hosted UI requires user action.");
                    }

                    _heartbeatFailureCount = 0;
                    _heartbeatConnectingCount = 0;
                    continue;
                }

                if (probe.Status == HeartbeatProbeStatus.Connecting)
                {
                    _heartbeatFailureCount = 0;
                    _heartbeatConnectingCount++;

                    if (_heartbeatConnectingCount < _heartbeatConnectingThreshold)
                    {
                        continue;
                    }

                    if (TryScheduleHeartbeatReload(probe.Message, preserveConnectingCounter: true))
                    {
                        return;
                    }

                    continue;
                }

                _heartbeatConnectingCount = 0;
                _heartbeatFailureCount++;
                _logger.Warning($"Heartbeat failure {_heartbeatFailureCount}/{_heartbeatFailureThreshold}.");

                if (_heartbeatFailureCount >= _heartbeatFailureThreshold &&
                    TryScheduleHeartbeatReload(probe.Message))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when StopHeartbeat() is called.
        }
        catch (Exception ex)
        {
            _logger.Error($"Heartbeat loop error: {ex.Message}");
        }
    }

    private async Task<HeartbeatProbeResult> ProbeGatewayHealthAsync(string url, CancellationToken token)
    {
        var hostedSessionResult = await ProbeHostedSessionAsync();
        if (hostedSessionResult is not null)
        {
            if (hostedSessionResult.Status is HeartbeatProbeStatus.Failure or HeartbeatProbeStatus.Connecting)
            {
                var transportResult = await ProbeGatewayTransportAsync(url, token);
                if (transportResult.Status == HeartbeatProbeStatus.Healthy)
                {
                    return hostedSessionResult with
                    {
                        Message = $"{hostedSessionResult.Message} {transportResult.Message}"
                    };
                }
            }

            return hostedSessionResult;
        }

        return await ProbeGatewayTransportAsync(url, token);
    }

    private static async Task<HeartbeatProbeResult> ProbeGatewayTransportAsync(string url, CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache, no-store, max-age=0");
            request.Headers.TryAddWithoutValidation("Pragma", "no-cache");
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");

            using var response = await HeartbeatHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            var statusCode = (int)response.StatusCode;
            var proxyHint = response.Headers.TryGetValues("cf-ray", out _) ? " via Cloudflare" : string.Empty;

            return statusCode switch
            {
                >= 200 and < 300 => HeartbeatProbeResult.Healthy($"Gateway reachable over HTTP{proxyHint} ({statusCode})."),
                301 or 302 or 303 or 307 or 308 => HeartbeatProbeResult.Healthy(
                    $"Gateway reachable over HTTP{proxyHint} but redirected ({statusCode})."),
                401 or 403 => HeartbeatProbeResult.Healthy(
                    $"Gateway reachable over HTTP{proxyHint} but requires authentication or origin approval ({statusCode})."),
                404 => HeartbeatProbeResult.Healthy(
                    $"Gateway reachable over HTTP{proxyHint} but the configured Control UI path returned 404."),
                405 => HeartbeatProbeResult.Healthy(
                    $"Gateway reachable over HTTP{proxyHint} but the proxy rejected the probe method ({statusCode})."),
                _ => HeartbeatProbeResult.Healthy(
                    $"Gateway reachable over HTTP{proxyHint} ({statusCode} {response.ReasonPhrase}).")
            };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return HeartbeatProbeResult.Failure($"Gateway heartbeat request failed: {ex.Message}");
        }
    }

    private async Task<HeartbeatProbeResult?> ProbeHostedSessionAsync()
    {
        if (!_isInitialized || GetCoreWebView() is null)
        {
            return null;
        }

        var snapshot = await InspectControlUiStateAsync();
        return snapshot.Phase switch
        {
            ControlUiPhase.Connected =>
                HeartbeatProbeResult.Healthy("Hosted Control UI reports an active Gateway session."),
            ControlUiPhase.AuthRequired or ControlUiPhase.PairingRequired or ControlUiPhase.OriginRejected =>
                HeartbeatProbeResult.SessionBlocked(snapshot.DetailOrSummary),
            ControlUiPhase.PageLoaded or ControlUiPhase.GatewayConnecting =>
                HeartbeatProbeResult.Connecting("Hosted Control UI is still reconnecting to the Gateway."),
            ControlUiPhase.GatewayError =>
                HeartbeatProbeResult.Failure(snapshot.DetailOrSummary),
            _ => null,
        };
    }

    private bool TryScheduleHeartbeatReload(string message, bool preserveConnectingCounter = false)
    {
        var heartbeatReloadCooldown = GetHeartbeatReloadCooldown();
        var elapsed = DateTimeOffset.UtcNow - _lastHeartbeatReloadAt;
        if (elapsed < heartbeatReloadCooldown)
        {
            var remaining = heartbeatReloadCooldown - elapsed;
            _logger.Warning($"Heartbeat auto-refresh suppressed for another {Math.Ceiling(remaining.TotalSeconds)}s to avoid reverse-proxy thrash.");
            _heartbeatFailureCount = _heartbeatFailureThreshold - 1;

            if (preserveConnectingCounter)
            {
                _heartbeatConnectingCount = _heartbeatConnectingThreshold - 1;
            }

            return false;
        }

        _lastHeartbeatReloadAt = DateTimeOffset.UtcNow;
        Interlocked.Increment(ref _heartbeatRecoveryRequests);
        _logger.Warning($"Heartbeat threshold reached, requesting session recovery. Reason: {message}");
        _logger.Info("heartbeat.recovery.count", new { total = HeartbeatRecoveryRequests, message });
        HeartbeatFailed?.Invoke(message);

        // Stop heartbeat; coordinator-driven recovery will restart it after the session stabilizes.
        StopHeartbeat();
        return true;
    }

    private TimeSpan GetHeartbeatReloadCooldown()
    {
        var seconds = _heartbeatHardRefreshCooldownSeconds;
        return TimeSpan.FromSeconds(Math.Max(0, seconds));
    }

    private void LogHeartbeatObservation(HeartbeatProbeResult result)
    {
        HeartbeatObserved?.Invoke(result);

        var observationKey = $"{result.Status}:{result.Message}";
        if (string.Equals(_lastHeartbeatObservationKey, observationKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastHeartbeatObservationKey = observationKey;

        switch (result.Status)
        {
            case HeartbeatProbeStatus.Healthy:
                _logger.Info(result.Message);
                break;
            case HeartbeatProbeStatus.SessionBlocked:
                _logger.Warning($"Heartbeat detected a session issue that requires user action: {result.Message}");
                break;
            case HeartbeatProbeStatus.Connecting:
                _logger.Info(result.Message);
                break;
            case HeartbeatProbeStatus.Failure:
                _logger.Warning(result.Message);
                break;
        }
    }
}
