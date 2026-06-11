// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Models;

/// <summary>
/// Connection recovery policy configuration.
/// Controls reconnection, resync, and refresh behavior.
/// </summary>
public class RecoveryPolicyOptions
{
    /// <summary>
    /// Gets or sets whether background resume recovery is enabled.
    /// Default is true.
    /// </summary>
    public bool EnableBackgroundResume { get; set; } = true;

    /// <summary>
    /// Gets or sets the minimum background duration (seconds) to trigger resume recovery.
    /// If the app returns to foreground after being hidden for less than this, no recovery runs.
    /// Default is 10 seconds.
    /// </summary>
    public int BackgroundResumeThresholdSeconds { get; set; } = 10;

    /// <summary>
    /// Gets or sets the maximum number of reconnect attempts before escalating to soft resync.
    /// Default is 3.
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the maximum number of soft resync attempts before escalating to hard refresh.
    /// Default is 2.
    /// </summary>
    public int MaxSoftResyncAttempts { get; set; } = 2;

    /// <summary>
    /// Gets or sets the delay before first reconnect attempt (milliseconds).
    /// Default is 1200ms.
    /// </summary>
    public int ReconnectDelayMs { get; set; } = 1200;

    /// <summary>
    /// Gets or sets the backoff multiplier for reconnect attempts.
    /// Each retry waits: previousDelay * backoff.
    /// Default is 2.0 (exponential backoff).
    /// </summary>
    public double ReconnectBackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Gets or sets the maximum reconnect delay (milliseconds).
    /// Default is 45000ms (45 seconds).
    /// </summary>
    public int MaxReconnectDelayMs { get; set; } = 45000;

    /// <summary>
    /// Gets or sets the minimum time between hard refresh attempts (seconds).
    /// Prevents refresh thrashing.
    /// Default is 75 seconds.
    /// </summary>
    public int HardRefreshCooldownSeconds { get; set; } = 75;
}

/// <summary>
/// Heartbeat configuration options.
/// </summary>
public class HeartbeatOptions
{
    /// <summary>
    /// Gets or sets whether heartbeat probing is enabled.
    /// Default is true.
    /// </summary>
    public bool EnableHeartbeat { get; set; } = true;

    /// <summary>
    /// Gets or sets the heartbeat probe interval in seconds.
    /// 0 disables heartbeat.
    /// Default is 45s.
    /// </summary>
    public int IntervalSeconds { get; set; } = 45;

    /// <summary>
    /// Gets or sets the number of consecutive heartbeat failures before triggering recovery.
    /// Default is 2.
    /// </summary>
    public int FailureThreshold { get; set; } = 2;

    /// <summary>
    /// Gets or sets the number of consecutive "connecting" states before triggering recovery.
    /// Default is 4.
    /// </summary>
    public int ConnectingThreshold { get; set; } = 4;
}

/// <summary>
/// Diagnostics configuration options.
/// </summary>
public class DiagnosticsOptions
{
    /// <summary>
    /// Gets or sets whether verbose recovery logging is enabled.
    /// When true, every recovery event is logged with full details.
    /// Default is false (summary only).
    /// </summary>
    public bool EnableVerboseRecoveryLogging { get; set; } = false;
}
