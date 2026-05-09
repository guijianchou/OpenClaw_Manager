// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

/// <summary>
/// Monitors work status transitions and fires a notification when a task completes
/// (LIVE -> IDLE transition sustained beyond the debounce window).
/// Does NOT fire on WAIT -> IDLE (startup/loading scenario).
/// </summary>
public sealed class TaskCompleteNotifier : IDisposable
{
    private readonly int _debounceMs;
    private string _lastStatus = "WAIT";
    private CancellationTokenSource? _debounceCts;
    private bool _isDisposed;

    public TaskCompleteNotifier(int debounceMs = 2000)
    {
        _debounceMs = debounceMs;
    }

    /// <summary>
    /// Raised when a LIVE -> IDLE transition is confirmed after the debounce period.
    /// </summary>
    public event Action? TaskCompleted;

    /// <summary>
    /// Call this whenever the work status text changes (LIVE, IDLE, WAIT).
    /// </summary>
    public void OnWorkStatusChanged(string newStatus)
    {
        if (_isDisposed)
        {
            return;
        }

        var previousStatus = _lastStatus;
        _lastStatus = newStatus;

        // Cancel any pending debounce if status changed away from IDLE
        if (!string.Equals(newStatus, "IDLE", StringComparison.OrdinalIgnoreCase))
        {
            CancelPendingDebounce();
            return;
        }

        // Only fire on LIVE -> IDLE, not WAIT -> IDLE
        if (!string.Equals(previousStatus, "LIVE", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Start debounce timer
        CancelPendingDebounce();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;
        _ = DebounceAndFireAsync(token);
    }

    public void Dispose()
    {
        _isDisposed = true;
        CancelPendingDebounce();
    }

    private async Task DebounceAndFireAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_debounceMs, token).ConfigureAwait(false);
            if (!token.IsCancellationRequested && !_isDisposed)
            {
                TaskCompleted?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            // Debounce was cancelled (status changed before timeout)
        }
    }

    private void CancelPendingDebounce()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
    }
}
