// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

internal sealed class WebViewRecreationService
{
    private readonly WebViewCircuitBreaker _circuitBreaker = new();
    private string? _pendingReason;
    private string? _deferredReason;
    private bool _activeReasonCancelled;

    public bool IsRecreating { get; private set; }

    public string? LastReason { get; private set; }

    public int TotalRecreations { get; private set; }

    public int MergedRequests { get; private set; }

    public bool HasPendingDeferredOrActiveWork =>
        _pendingReason is not null ||
        _deferredReason is not null ||
        IsRecreating;

    public WebViewRecreationScheduleResult Schedule(string reason)
    {
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;

        if (normalizedReason.Contains("settings", StringComparison.OrdinalIgnoreCase) ||
            normalizedReason.Contains("initial", StringComparison.OrdinalIgnoreCase))
        {
            _circuitBreaker.Reset();
        }

        if (_pendingReason is not null)
        {
            MergedRequests++;
        }

        var pendingReason = ChooseHigherPriorityReason(_pendingReason, normalizedReason);
        _pendingReason = pendingReason;

        return new WebViewRecreationScheduleResult(
            ShouldStartTimer: !IsRecreating,
            Reason: pendingReason,
            IsRecreating: IsRecreating,
            MergedRequests: MergedRequests);
    }

    public WebViewRecreationBeginResult TryBegin(bool hasWebViewHostChild)
    {
        if (IsRecreating)
        {
            return WebViewRecreationBeginResult.NoWork;
        }

        if (!_circuitBreaker.CanAttempt())
        {
            return WebViewRecreationBeginResult.CircuitBreakerTripped(LastReason, TotalRecreations);
        }

        var previousReason = LastReason;
        var pendingReason = _pendingReason;
        _pendingReason = null;

        if (pendingReason is null)
        {
            if (!hasWebViewHostChild)
            {
                pendingReason = "implicit_initial_load";
            }
            else
            {
                return WebViewRecreationBeginResult.NoWork;
            }
        }

        IsRecreating = true;
        LastReason = pendingReason;
        return WebViewRecreationBeginResult.Begin(pendingReason, previousReason);
    }

    public string Defer(string reason)
    {
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;
        var deferredReason = ChooseHigherPriorityReason(_deferredReason, normalizedReason);
        _deferredReason = deferredReason;
        return deferredReason;
    }

    public bool TryConsumeDeferred(out string? reason)
    {
        reason = _deferredReason;
        if (reason is null)
        {
            return false;
        }

        _deferredReason = null;
        return true;
    }

    public void ClearPending()
    {
        _pendingReason = null;
        _deferredReason = null;
        _activeReasonCancelled = IsRecreating;
    }

    public bool TryCancelNavigationTimeoutRecovery(out WebViewRecreationCancelledRecoveryResult result)
    {
        string? pendingReason = null;
        if (IsNavigationTimeoutRecoveryReason(_pendingReason))
        {
            pendingReason = _pendingReason;
            _pendingReason = null;
        }

        string? deferredReason = null;
        if (IsNavigationTimeoutRecoveryReason(_deferredReason))
        {
            deferredReason = _deferredReason;
            _deferredReason = null;
        }

        string? activeReason = null;
        if (IsRecreating && IsNavigationTimeoutRecoveryReason(LastReason))
        {
            activeReason = LastReason;
            _activeReasonCancelled = true;
        }

        result = new WebViewRecreationCancelledRecoveryResult(pendingReason, deferredReason, activeReason);
        return result.CancelledPending || result.CancelledDeferred || result.CancelledActive;
    }

    public static bool IsNavigationTimeoutRecoveryReason(string? reason)
    {
        return reason is "navigation_start_timeout" or "navigation_completion_timeout";
    }

    public static string ChooseHigherPriorityReason(string? currentReason, string newReason)
    {
        if (string.IsNullOrWhiteSpace(currentReason))
        {
            return newReason;
        }

        return GetReasonPriority(newReason) >= GetReasonPriority(currentReason)
            ? newReason
            : currentReason;
    }

    private static int GetReasonPriority(string reason)
    {
        if (IsNavigationTimeoutRecoveryReason(reason))
        {
            return 0;
        }

        if (reason.Contains("settings", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("environment", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("session", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("initial", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("topology", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 1;
    }

    public bool ShouldSkipCurrentRecreation()
    {
        return _activeReasonCancelled;
    }

    public bool CanAttemptInLoop()
    {
        return _circuitBreaker.CanAttempt();
    }

    public void RecordAttempt()
    {
        TotalRecreations++;
        _circuitBreaker.RecordAttempt();
    }

    public void RecordAttempt(string reason)
    {
        LastReason = string.IsNullOrWhiteSpace(reason) ? LastReason : reason;
        RecordAttempt();
    }

    public bool TryConsumeQueued(out string? reason)
    {
        reason = _pendingReason;
        if (reason is null)
        {
            return false;
        }

        LastReason = reason;
        _pendingReason = null;
        return true;
    }

    public WebViewRecreationFinishResult Finish()
    {
        IsRecreating = false;
        _activeReasonCancelled = false;
        return new WebViewRecreationFinishResult(
            LastReason,
            _pendingReason,
            TotalRecreations,
            MergedRequests);
    }
}

internal readonly record struct WebViewRecreationScheduleResult(
    bool ShouldStartTimer,
    string Reason,
    bool IsRecreating,
    int MergedRequests);

internal readonly record struct WebViewRecreationBeginResult(
    bool ShouldBegin,
    bool IsCircuitBreakerTripped,
    string? Reason,
    string? LastReason,
    int TotalRecreations)
{
    public static WebViewRecreationBeginResult NoWork =>
        new(false, false, null, null, 0);

    public static WebViewRecreationBeginResult Begin(string reason, string? lastReason) =>
        new(true, false, reason, lastReason, 0);

    public static WebViewRecreationBeginResult CircuitBreakerTripped(string? lastReason, int totalRecreations) =>
        new(false, true, null, lastReason, totalRecreations);
}

internal readonly record struct WebViewRecreationFinishResult(
    string? LastReason,
    string? PendingReason,
    int TotalRecreations,
    int MergedRequests);

internal readonly record struct WebViewRecreationCancelledRecoveryResult(
    string? PendingReason,
    string? DeferredReason,
    string? ActiveReason)
{
    public bool CancelledPending => PendingReason is not null;

    public bool CancelledDeferred => DeferredReason is not null;

    public bool CancelledActive => ActiveReason is not null;
}
