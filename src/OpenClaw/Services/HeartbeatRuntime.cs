// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

internal sealed class HeartbeatRuntime : IDisposable
{
    private readonly IAppLogger _logger;
    private CancellationTokenSource? _cancellation;
    private Task? _task;
    private string? _key;

    public HeartbeatRuntime(IAppLogger logger)
    {
        _logger = logger;
    }

    public bool IsRunning => _task is { IsCompleted: false };

    public bool IsSameRun(string key)
    {
        return IsRunning && string.Equals(_key, key, StringComparison.Ordinal);
    }

    public void Start(string key, Func<CancellationToken, Task> loop)
    {
        Stop();
        var cancellation = new CancellationTokenSource();
        _key = key;
        _cancellation = cancellation;
        _task = Task.Run(() => RunObservedAsync(key, loop, cancellation));
    }

    public void Stop()
    {
        var cancellation = _cancellation;
        var task = _task;
        _cancellation = null;
        _task = null;
        _key = null;

        cancellation?.Cancel();
        if (task is null)
        {
            cancellation?.Dispose();
            return;
        }

        _ = ObserveStopAsync(task, cancellation);
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task RunObservedAsync(
        string key,
        Func<CancellationToken, Task> loop,
        CancellationTokenSource cancellation)
    {
        try
        {
            await loop(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Expected during Stop().
        }
        catch (Exception ex)
        {
            _logger.Error($"Heartbeat loop error for run '{key}': {ex.Message}");
        }
    }

    private async Task ObserveStopAsync(Task task, CancellationTokenSource? cancellation)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during Stop().
        }
        catch (Exception ex)
        {
            _logger.Error($"Heartbeat loop shutdown error: {ex.Message}");
        }
        finally
        {
            cancellation?.Dispose();
        }
    }
}
