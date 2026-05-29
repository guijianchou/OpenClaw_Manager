// Copyright (c) Lanstack @openclaw. All rights reserved.

using OpenClaw.Models;

namespace OpenClaw.Services;

public sealed partial class ShellSessionCoordinator
{
    /// <summary>
    /// Called when WebView reports navigation completed.
    /// </summary>
    private void OnNavigationCompleted(string? uri) => HandleNavigationCompleted(uri);

    /// <summary>
    /// Called when hosted UI reports ready state.
    /// </summary>
    private void OnHostedUiStateUpdated(ControlUiProbeSnapshot snapshot) => HandleHostedUiStateUpdated(snapshot);

    /// <summary>
    /// Called when hosted UI reports session ready.
    /// </summary>
    private void OnSessionReady(SessionReadyEventArgs args) => HandleSessionReady(args);

    /// <summary>
    /// Called when an event gap is detected.
    /// </summary>
    private void OnEventGapDetected(EventGapEventArgs args) =>
        SafeFireAndForget(token => HandleEventGapDetectedAsync(args, token), "stream.gap.recovery");

    /// <summary>
    /// Called when connection state changes.
    /// </summary>
    private void OnConnectionStateChanged(ConnectionState state) => HandleConnectionStateChanged(state);

    /// <summary>
    /// Called when a navigation error occurs.
    /// </summary>
    private void OnNavigationError(string message) => HandleNavigationError(message);

    /// <summary>
    /// Updates heartbeat observation.
    /// </summary>
    private void OnHeartbeatObserved(HeartbeatProbeResult result) => HandleHeartbeatObserved(result);

    private void OnHeartbeatFailed(string message) =>
        SafeFireAndForget(token => HandleHeartbeatFailedAsync(message, token), "heartbeat.recovery");

    private void SafeFireAndForget(Func<CancellationToken, Task> operation, string eventName)
    {
        var cancellation = CreateObservedOperationCancellation();
        if (cancellation is null)
        {
            return;
        }

        _ = RunObservedAsync(operation, eventName, cancellation);
    }

    private async Task RunObservedAsync(
        Func<CancellationToken, Task> operation,
        string eventName,
        CancellationTokenSource cancellation)
    {
        try
        {
            if (_isDisposed)
            {
                return;
            }

            await operation(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.Info($"{eventName}.cancelled");
        }
        catch (ObjectDisposedException ex)
        {
            _logger.Info($"{eventName}.disposed", new { ex.ObjectName });
        }
        catch (Exception ex)
        {
            _logger.Error($"{eventName}.fail", new { ex.Message });
        }
        finally
        {
            ReleaseObservedOperationCancellation(cancellation);
        }
    }

    private CancellationTokenSource? CreateObservedOperationCancellation()
    {
        var cancellation = new CancellationTokenSource();
        lock (_observedOperationGate)
        {
            if (_isDisposed)
            {
                cancellation.Dispose();
                return null;
            }

            _observedOperationCancellations.Add(cancellation);
        }

        return cancellation;
    }

    private void ReleaseObservedOperationCancellation(CancellationTokenSource cancellation)
    {
        lock (_observedOperationGate)
        {
            _observedOperationCancellations.Remove(cancellation);
        }

        cancellation.Dispose();
    }

    private void CancelObservedOperations()
    {
        CancellationTokenSource[] cancellations;

        lock (_observedOperationGate)
        {
            cancellations = _observedOperationCancellations.ToArray();
        }

        foreach (var cancellation in cancellations)
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The observed operation owns disposal and may have completed.
            }
        }
    }
}
