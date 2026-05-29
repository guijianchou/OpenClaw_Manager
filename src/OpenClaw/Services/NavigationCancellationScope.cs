// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

internal sealed class NavigationCancellationScope
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _source = new();
    private int _leaseCount;
    private bool _isRetired;
    private bool _isDisposed;

    public Lease? TryAcquire()
    {
        lock (_gate)
        {
            if (_isRetired || _isDisposed || _source.IsCancellationRequested)
            {
                return null;
            }

            _leaseCount++;
            return new Lease(this, _source.Token);
        }
    }

    public void CancelAndRetire()
    {
        Lease? cancelLease;

        lock (_gate)
        {
            if (_isRetired || _isDisposed)
            {
                return;
            }

            _isRetired = true;
            _leaseCount++;
            cancelLease = new Lease(this, _source.Token);
        }

        try
        {
            _source.Cancel();
        }
        catch (AggregateException)
        {
            // Cancellation is best-effort cleanup; callbacks must not block scope retirement.
        }
        finally
        {
            cancelLease.Dispose();
        }
    }

    private void Release()
    {
        CancellationTokenSource? sourceToDispose = null;

        lock (_gate)
        {
            if (_leaseCount > 0)
            {
                _leaseCount--;
            }

            if (_isRetired && _leaseCount == 0 && !_isDisposed)
            {
                _isDisposed = true;
                sourceToDispose = _source;
            }
        }

        sourceToDispose?.Dispose();
    }

    internal sealed class Lease : IDisposable
    {
        private NavigationCancellationScope? _owner;

        internal Lease(NavigationCancellationScope owner, CancellationToken token)
        {
            _owner = owner;
            Token = token;
        }

        public CancellationToken Token { get; }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Release();
        }
    }
}
