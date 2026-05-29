// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

internal sealed partial class WebViewStatusInspector
{
    private async Task ProbeControlUiStateAfterNavigationAsync(CancellationTokenSource cancellation, int generation)
    {
        var cancellationToken = cancellation.Token;
        var delays = new[]
        {
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(8),
        };

        try
        {
            foreach (var delay in delays)
            {
                await Task.Delay(delay, cancellationToken);
                if (!_generations.IsCurrent(generation))
                {
                    return;
                }

                var snapshot = await _uiDispatcher.RunAsync(
                    () => InspectAsync(cancellationToken, generation, publishSnapshot: true),
                    cancellationToken);
                if (snapshot.IsTerminal)
                {
                    return;
                }
            }

            PublishProbeExhaustedSnapshot(generation);
        }
        catch (OperationCanceledException)
        {
            // Expected when navigation changes.
        }
        catch (ObjectDisposedException)
        {
            // Expected when WebView2 is torn down during navigation/recreation.
        }
        catch (Exception ex)
        {
            if (_generations.IsCurrent(generation))
            {
                _logger.Warning($"WebView status probe loop failed: {ex.Message}");
            }
        }
        finally
        {
            lock (_probeGate)
            {
                if (ReferenceEquals(_statusProbeCts, cancellation))
                {
                    _statusProbeCts = null;
                    _statusProbeTask = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private void PublishProbeExhaustedSnapshot(int generation)
    {
        TrySetUnavailableSnapshot(
            "Control UI did not report a terminal session state after navigation probes were exhausted.",
            generation);
    }
}
