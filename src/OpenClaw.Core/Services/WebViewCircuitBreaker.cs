// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

/// <summary>
/// Prevents runaway WebView2 recreation by limiting attempts within a sliding time window.
/// When tripped, CanAttempt returns false until the window expires or Reset is called.
/// </summary>
public sealed class WebViewCircuitBreaker
{
    private readonly int _maxAttempts;
    private readonly TimeSpan _window;
    private readonly Queue<DateTimeOffset> _attempts = new();

    public WebViewCircuitBreaker(int maxAttempts = 5, int windowSeconds = 60)
    {
        _maxAttempts = maxAttempts;
        _window = TimeSpan.FromSeconds(windowSeconds);
    }

    /// <summary>
    /// Gets whether the circuit breaker is currently tripped (too many recent attempts).
    /// </summary>
    public bool IsTripped => !CanAttempt();

    /// <summary>
    /// Returns true if a recreation attempt is allowed (not tripped).
    /// </summary>
    public bool CanAttempt()
    {
        PruneExpired();
        return _attempts.Count < _maxAttempts;
    }

    /// <summary>
    /// Records a recreation attempt timestamp.
    /// </summary>
    public void RecordAttempt()
    {
        _attempts.Enqueue(DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Resets the circuit breaker, clearing all recorded attempts.
    /// Call this when the user manually triggers a reload.
    /// </summary>
    public void Reset()
    {
        _attempts.Clear();
    }

    private void PruneExpired()
    {
        var cutoff = DateTimeOffset.UtcNow - _window;
        while (_attempts.Count > 0 && _attempts.Peek() < cutoff)
        {
            _attempts.Dequeue();
        }
    }
}
