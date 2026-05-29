// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.IO.Pipes;
using System.Text;

namespace OpenClaw.Services;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private const string ActivationCommand = "activate";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);
    private readonly Semaphore? _singleInstanceLock;
    private readonly bool _ownsLock;
    private readonly string _pipeName;
    private readonly IAppLogger _logger;
    private readonly object _listenerGate = new();
    private NamedPipeServerStream? _activeServer;
    private CancellationTokenSource? _listenCancellation;
    private Task? _listenTask;
    private bool _isDisposed;

    private SingleInstanceCoordinator(Semaphore? singleInstanceLock, bool ownsLock, string pipeName, IAppLogger logger)
    {
        _singleInstanceLock = singleInstanceLock;
        _ownsLock = ownsLock;
        _pipeName = pipeName;
        _logger = logger;
    }

    public const string DefaultLockName = @"Local\OpenClaw.Manager.SingleInstance";

    public const string DefaultPipeName = "OpenClaw.Manager.SingleInstance";

    public event Action? ActivationRequested;

    public bool IsPrimary => _ownsLock;

    public static SingleInstanceCoordinator CreatePrimaryOrSecondary(IAppLogger logger) =>
        CreatePrimaryOrSecondary(DefaultLockName, DefaultPipeName, logger);

    public static Task<SingleInstanceCoordinator?> TryCreatePrimaryAfterActivationFailureAsync(
        IAppLogger logger,
        CancellationToken cancellationToken = default) =>
        TryCreatePrimaryAsync(DefaultLockName, DefaultPipeName, logger, TimeSpan.FromSeconds(3), cancellationToken);

    public static SingleInstanceCoordinator CreatePrimaryOrSecondary(string lockName, string pipeName, IAppLogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(logger);

        var singleInstanceLock = TryCreateSingleInstanceLock(lockName, logger);
        if (singleInstanceLock is null)
        {
            return new SingleInstanceCoordinator(singleInstanceLock: null, ownsLock: false, pipeName, logger);
        }

        var ownsLock = TryOwnLock(singleInstanceLock);
        return new SingleInstanceCoordinator(singleInstanceLock, ownsLock, pipeName, logger);
    }

    public static async Task<SingleInstanceCoordinator?> TryCreatePrimaryAsync(
        string lockName,
        string pipeName,
        IAppLogger logger,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(logger);

        var deadline = DateTimeOffset.UtcNow + timeout;
        var singleInstanceLock = await TryCreateSingleInstanceLockAsync(
            lockName,
            logger,
            deadline,
            cancellationToken).ConfigureAwait(false);
        if (singleInstanceLock is null)
        {
            return null;
        }

        bool ownsLock;
        try
        {
            ownsLock = await TryOwnLockAsync(singleInstanceLock, deadline, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            singleInstanceLock.Dispose();
            throw;
        }

        if (!ownsLock)
        {
            singleInstanceLock.Dispose();
            return null;
        }

        return new SingleInstanceCoordinator(singleInstanceLock, ownsLock: true, pipeName, logger);
    }

    public static Task<bool> RequestActivationOfPrimaryInstanceAsync(
        IAppLogger logger,
        CancellationToken cancellationToken = default) =>
        RequestActivationOfPrimaryInstanceAsync(DefaultPipeName, logger, cancellationToken: cancellationToken);

    public static async Task<bool> RequestActivationOfPrimaryInstanceAsync(
        string pipeName,
        IAppLogger logger,
        int timeoutMilliseconds = 750,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            await pipe.ConnectAsync(timeoutMilliseconds, cancellationToken).ConfigureAwait(false);
            using var writer = new StreamWriter(pipe, Encoding.UTF8) { AutoFlush = true };
            await writer.WriteLineAsync(ActivationCommand).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            logger.Warning($"Failed to request primary instance activation: {ex.Message}");
            return false;
        }
    }

    public void StartListening()
    {
        if (_isDisposed || !IsPrimary || _listenTask is not null)
        {
            return;
        }

        _listenCancellation = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenForActivationRequestsAsync(_listenCancellation.Token));
    }

    /// <summary>
    /// Cancels the listener and waits for the listen task to complete.
    /// Call this before Dispose to avoid pipe listener races on rapid restart.
    /// </summary>
    public async Task StopAsync()
    {
        _listenCancellation?.Cancel();
        DisposeActiveServer();

        if (_listenTask is not null)
        {
            try
            {
                await _listenTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            catch (ObjectDisposedException)
            {
                // Expected when StopAsync disposes the active pipe server.
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Single-instance listener dispose drain failed: {ex.Message}");
        }

        if (_ownsLock && _singleInstanceLock is not null)
        {
            try
            {
                _singleInstanceLock.Release();
            }
            catch (SemaphoreFullException ex)
            {
                _logger.Warning($"Single-instance lock was already released: {ex.Message}");
            }
        }

        _listenCancellation?.Dispose();
        _singleInstanceLock?.Dispose();
    }

    private async Task ListenForActivationRequestsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                SetActiveServer(server);

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var command = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.Equals(command, ActivationCommand, StringComparison.Ordinal))
                {
                    ActivationRequested?.Invoke();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warning($"Single-instance activation listener failed: {ex.Message}");
            }
            finally
            {
                ClearActiveServer();
            }
        }
    }

    private void SetActiveServer(NamedPipeServerStream server)
    {
        lock (_listenerGate)
        {
            _activeServer = server;
        }
    }

    private void ClearActiveServer()
    {
        lock (_listenerGate)
        {
            _activeServer = null;
        }
    }

    private void DisposeActiveServer()
    {
        NamedPipeServerStream? activeServer;
        lock (_listenerGate)
        {
            activeServer = _activeServer;
            _activeServer = null;
        }

        try
        {
            activeServer?.Dispose();
        }
        catch (IOException)
        {
            // Best effort shutdown path.
        }
        catch (ObjectDisposedException)
        {
            // Already disposed by the listener loop.
        }
    }

    private static Semaphore? TryCreateSingleInstanceLock(string lockName, IAppLogger logger)
    {
        try
        {
            return new Semaphore(initialCount: 1, maximumCount: 1, lockName);
        }
        catch (WaitHandleCannotBeOpenedException ex)
        {
            logger.Warning($"Single-instance lock name is held by a legacy single-instance object; treating this launch as secondary until it exits: {ex.Message}");
            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            logger.Warning($"Single-instance lock could not be opened; treating this launch as secondary: {ex.Message}");
            return null;
        }
    }

    private static async Task<Semaphore?> TryCreateSingleInstanceLockAsync(
        string lockName,
        IAppLogger logger,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        var loggedLegacyConflict = false;

        try
        {
            while (true)
            {
                try
                {
                    return new Semaphore(initialCount: 1, maximumCount: 1, lockName);
                }
                catch (WaitHandleCannotBeOpenedException ex)
                {
                    if (!loggedLegacyConflict)
                    {
                        logger.Warning($"Single-instance lock name is held by a legacy single-instance object; treating this launch as secondary until it exits: {ex.Message}");
                        loggedLegacyConflict = true;
                    }

                    if (DateTimeOffset.UtcNow >= deadline)
                    {
                        return null;
                    }

                    await Task.Delay(GetRetryDelay(deadline), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            logger.Warning($"Single-instance lock could not be opened; treating this launch as secondary: {ex.Message}");
            return null;
        }
    }

    private static bool TryOwnLock(Semaphore singleInstanceLock)
    {
        return singleInstanceLock.WaitOne(TimeSpan.Zero);
    }

    private static async Task<bool> TryOwnLockAsync(
        Semaphore singleInstanceLock,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryOwnLock(singleInstanceLock))
            {
                return true;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(GetRetryDelay(deadline), cancellationToken).ConfigureAwait(false);
        }
    }

    private static TimeSpan GetRetryDelay(DateTimeOffset deadline)
    {
        var remaining = deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return remaining < RetryDelay ? remaining : RetryDelay;
    }
}
