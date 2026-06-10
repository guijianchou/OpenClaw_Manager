// Copyright (c) Lanstack @openclaw. All rights reserved.

using OpenClaw.Models;

namespace OpenClaw.Services;

public partial class WebViewService
{
    private const int DefaultHeartbeatFailureThreshold = 3;
    private const int DefaultHeartbeatConnectingThreshold = 3;

    private readonly HeartbeatRuntime _heartbeatRuntime;
    private readonly GatewayHeartbeatTransport _heartbeatTransport;
    private readonly HostedSessionHeartbeatPolicy _hostedSessionHeartbeatPolicy;
    private readonly object _heartbeatStateGate = new();
    private int _heartbeatFailureCount;
    private int _heartbeatConnectingCount;
    private string? _lastHeartbeatObservationKey;
    private string? _heartbeatGatewayUrl;
    private int _heartbeatIntervalSeconds;
    private int _heartbeatFailureThreshold = DefaultHeartbeatFailureThreshold;
    private int _heartbeatConnectingThreshold = DefaultHeartbeatConnectingThreshold;
    private static readonly TimeSpan DefaultHeartbeatReloadCooldown = TimeSpan.FromSeconds(75);
    private int _heartbeatHardRefreshCooldownSeconds = (int)DefaultHeartbeatReloadCooldown.TotalSeconds;
    private DateTimeOffset _lastHeartbeatReloadAt = DateTimeOffset.MinValue;
    private string? _lastStartHeartbeatKey;
    private int _heartbeatRecoveryRequests;
    private int _heartbeatRunId;

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
        lock (_heartbeatStateGate)
        {
            if (_heartbeatRuntime.IsSameRun(heartbeatStartKey))
            {
                return;
            }

            StopHeartbeatCore();

            _heartbeatFailureCount = 0;
            _heartbeatConnectingCount = 0;
            _lastHeartbeatObservationKey = null;
            _heartbeatGatewayUrl = gatewayUrl;
            _heartbeatIntervalSeconds = intervalSeconds;
            _heartbeatFailureThreshold = Math.Max(1, heartbeatSettings.FailureThreshold);
            _heartbeatConnectingThreshold = Math.Max(1, heartbeatSettings.ConnectingThreshold);
            _heartbeatHardRefreshCooldownSeconds = Math.Max(0, recoveryPolicyOptions.HardRefreshCooldownSeconds);
            var heartbeatRunId = Interlocked.Increment(ref _heartbeatRunId);

            if (!string.Equals(_lastStartHeartbeatKey, heartbeatStartKey, StringComparison.Ordinal))
            {
                _lastStartHeartbeatKey = heartbeatStartKey;
                _logger.Info($"Heartbeat started: interval={intervalSeconds}s, failureThreshold={_heartbeatFailureThreshold}, connectingThreshold={_heartbeatConnectingThreshold}, url={gatewayUrl}");
            }

            _heartbeatRuntime.Start(
                heartbeatStartKey,
                token => RunSessionAwareHeartbeatLoopAsync(gatewayUrl, TimeSpan.FromSeconds(intervalSeconds), heartbeatRunId, token));
        }
    }

    /// <summary>
    /// Stops the periodic heartbeat probe.
    /// </summary>
    public void StopHeartbeat()
    {
        lock (_heartbeatStateGate)
        {
            StopHeartbeatCore();
        }
    }

    private bool TryStopHeartbeatForRecovery(int runId)
    {
        lock (_heartbeatStateGate)
        {
            if (!IsCurrentHeartbeatRun(runId))
            {
                return false;
            }

            StopHeartbeatCore();
            return true;
        }
    }

    private void StopHeartbeatCore()
    {
        Interlocked.Increment(ref _heartbeatRunId);
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

    private async Task RunSessionAwareHeartbeatLoopAsync(string gatewayUrl, TimeSpan interval, int runId, CancellationToken token)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            if (!await ProcessHeartbeatTickAsync(gatewayUrl, runId, token))
            {
                return;
            }

            while (await timer.WaitForNextTickAsync(token))
            {
                if (!await ProcessHeartbeatTickAsync(gatewayUrl, runId, token))
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

    private async Task<bool> ProcessHeartbeatTickAsync(string gatewayUrl, int runId, CancellationToken token)
    {
        if (token.IsCancellationRequested || !IsCurrentHeartbeatRun(runId))
        {
            return false;
        }

        var probe = await ProbeGatewayHealthAsync(gatewayUrl, token);
        token.ThrowIfCancellationRequested();
        if (!IsCurrentHeartbeatRun(runId))
        {
            return false;
        }

        LogHeartbeatObservation(probe);

        if (probe.Status == HeartbeatProbeStatus.Healthy)
        {
            if (_heartbeatFailureCount > 0)
            {
                _logger.Info($"Heartbeat recovered after {_heartbeatFailureCount} failure(s).");
            }

            _heartbeatFailureCount = 0;
            _heartbeatConnectingCount = 0;
            return true;
        }

        if (probe.Status == HeartbeatProbeStatus.SessionBlocked)
        {
            if (_heartbeatFailureCount > 0)
            {
                _logger.Info("Heartbeat failure counter reset because the hosted UI requires user action.");
            }

            _heartbeatFailureCount = 0;
            _heartbeatConnectingCount = 0;
            return true;
        }

        if (probe.Status == HeartbeatProbeStatus.Connecting)
        {
            _heartbeatFailureCount = 0;
            _heartbeatConnectingCount++;

            if (_heartbeatConnectingCount < _heartbeatConnectingThreshold)
            {
                return true;
            }

            return !TryScheduleHeartbeatReload(probe.Message, runId, preserveConnectingCounter: true);
        }

        _heartbeatConnectingCount = 0;
        _heartbeatFailureCount++;
        _logger.Warning($"Heartbeat failure {_heartbeatFailureCount}/{_heartbeatFailureThreshold}.");

        if (_heartbeatFailureCount >= _heartbeatFailureThreshold)
        {
            return !TryScheduleHeartbeatReload(probe.Message, runId);
        }

        return true;
    }

    private async Task<HeartbeatProbeResult> ProbeGatewayHealthAsync(string url, CancellationToken token)
    {
        var hostedSessionResult = await ProbeHostedSessionAsync(token);
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

    private Task<HeartbeatProbeResult> ProbeGatewayTransportAsync(string url, CancellationToken token)
    {
        return _heartbeatTransport.ProbeAsync(url, token);
    }

    private async Task<HeartbeatProbeResult?> ProbeHostedSessionAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (!_isInitialized)
        {
            return null;
        }

        var snapshot = await InspectControlUiStateAsync(token, publishSnapshot: false);
        token.ThrowIfCancellationRequested();
        return _hostedSessionHeartbeatPolicy.Map(snapshot);
    }

    private bool TryScheduleHeartbeatReload(string message, int runId, bool preserveConnectingCounter = false)
    {
        if (!IsCurrentHeartbeatRun(runId))
        {
            return true;
        }

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

        // Stop heartbeat first; coordinator-driven recovery will restart it after the session stabilizes.
        if (!TryStopHeartbeatForRecovery(runId))
        {
            return true;
        }

        _lastHeartbeatReloadAt = DateTimeOffset.UtcNow;
        Interlocked.Increment(ref _heartbeatRecoveryRequests);
        _logger.Warning($"Heartbeat threshold reached, requesting session recovery. Reason: {message}");
        _logger.Info("heartbeat.recovery.count", new { total = HeartbeatRecoveryRequests, message });
        HeartbeatFailed?.Invoke(message);
        return true;
    }

    private TimeSpan GetHeartbeatReloadCooldown()
    {
        var seconds = _heartbeatHardRefreshCooldownSeconds;
        return TimeSpan.FromSeconds(Math.Max(0, seconds));
    }

    private bool IsCurrentHeartbeatRun(int runId)
    {
        lock (_heartbeatStateGate)
        {
            return !_isDisposed && Volatile.Read(ref _heartbeatRunId) == runId;
        }
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
