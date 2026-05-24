// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

internal sealed class WebViewRecreationService
{
    private readonly WebViewCircuitBreaker _circuitBreaker = new();
    private string? _pendingReason;

    public bool IsRecreating { get; private set; }

    public string? LastReason { get; private set; }

    public int TotalRecreations { get; private set; }

    public int MergedRequests { get; private set; }

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

        _pendingReason = normalizedReason;

        return new WebViewRecreationScheduleResult(
            ShouldStartTimer: !IsRecreating,
            Reason: normalizedReason,
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

    public bool CanAttemptInLoop()
    {
        return _circuitBreaker.CanAttempt();
    }

    public void RecordAttempt()
    {
        TotalRecreations++;
        _circuitBreaker.RecordAttempt();
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
