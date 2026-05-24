// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace OpenClaw.Services;

internal sealed class WebViewStatusInspector : IDisposable
{
    private const string ControlUiStatusMessageKind = "openclaw-control-ui-status";
    private static readonly TimeSpan InspectionReuseWindow = TimeSpan.FromMilliseconds(350);

    private readonly Func<CoreWebView2?> _getCoreWebView;
    private readonly WebViewGenerationTracker _generations;
    private readonly IAppLogger _logger;
    private readonly object _inspectionGate = new();

    private CancellationTokenSource? _statusProbeCts;
    private Task<ControlUiProbeSnapshot>? _inFlightInspectionTask;
    private int _inFlightInspectionGeneration;
    private DateTimeOffset _lastControlUiInspectionAt = DateTimeOffset.MinValue;
    private ControlUiProbeSnapshot _latestControlUiSnapshot = ControlUiProbeSnapshot.Unknown;
    private int _latestControlUiSnapshotGeneration;
    private int _totalControlUiInspectionRequests;
    private int _cachedControlUiInspectionRequests;
    private int _coalescedControlUiInspectionRequests;

    public WebViewStatusInspector(
        Func<CoreWebView2?> getCoreWebView,
        WebViewGenerationTracker generations,
        IAppLogger logger)
    {
        _getCoreWebView = getCoreWebView;
        _generations = generations;
        _logger = logger;
    }

    public event Action<ControlUiProbeSnapshot>? SnapshotUpdated;

    public ControlUiProbeSnapshot LatestSnapshot => _latestControlUiSnapshot;

    public int TotalRequests => Volatile.Read(ref _totalControlUiInspectionRequests);

    public int CachedRequests => Volatile.Read(ref _cachedControlUiInspectionRequests);

    public int CoalescedRequests => Volatile.Read(ref _coalescedControlUiInspectionRequests);

    public Task<ControlUiProbeSnapshot> InspectAsync(CancellationToken cancellationToken = default)
    {
        return InspectAsync(cancellationToken, _generations.Current);
    }

    public void StartProbeLoop()
    {
        CancelProbeLoop();
        _statusProbeCts = new CancellationTokenSource();
        var generation = _generations.Current;
        _ = ProbeControlUiStateAfterNavigationAsync(_statusProbeCts.Token, generation);
    }

    public void CancelProbeLoop()
    {
        if (_statusProbeCts is not null)
        {
            _statusProbeCts.Cancel();
            _statusProbeCts.Dispose();
            _statusProbeCts = null;
        }
    }

    public void InvalidateCache()
    {
        lock (_inspectionGate)
        {
            _lastControlUiInspectionAt = DateTimeOffset.MinValue;
            _latestControlUiSnapshotGeneration = 0;
            _inFlightInspectionTask = null;
        }
    }

    public void SetLoadingSnapshot(string? uri)
    {
        ApplyControlUiSnapshot(ControlUiProbeSnapshot.Loading(uri), _generations.Current, notifySnapshotUpdated: true);
    }

    public void SetPageLoadedSnapshot(string? uri)
    {
        ApplyControlUiSnapshot(ControlUiProbeSnapshot.PageLoaded(uri), _generations.Current, notifySnapshotUpdated: true);
    }

    public void SetUnavailableSnapshot(string summary)
    {
        ApplyControlUiSnapshot(ControlUiProbeSnapshot.Unavailable(summary), _generations.Current, notifySnapshotUpdated: true);
    }

    public void SetUnknownSnapshot()
    {
        ApplyControlUiSnapshot(ControlUiProbeSnapshot.Unknown, _generations.Current, notifySnapshotUpdated: false);
    }

    public bool TryApplyHostMessage(string json, out ControlUiProbeSnapshot snapshot)
    {
        snapshot = ParseControlUiSnapshot(json);
        if (snapshot.Phase == ControlUiPhase.Unknown)
        {
            return false;
        }

        ApplyControlUiSnapshot(snapshot, _generations.Current, notifySnapshotUpdated: false);
        return true;
    }

    public void Dispose()
    {
        CancelProbeLoop();
    }

    private Task<ControlUiProbeSnapshot> InspectAsync(CancellationToken cancellationToken, int generation)
    {
        Interlocked.Increment(ref _totalControlUiInspectionRequests);
        if (cancellationToken.IsCancellationRequested || !_generations.IsCurrent(generation))
        {
            return Task.FromResult(ControlUiProbeSnapshot.Unknown);
        }

        var coreWebView = _getCoreWebView();
        if (coreWebView is null)
        {
            return Task.FromResult(ControlUiProbeSnapshot.Unavailable("WebView2 is not initialized."));
        }

        lock (_inspectionGate)
        {
            if (_inFlightInspectionTask is not null &&
                _inFlightInspectionGeneration == generation)
            {
                var coalescedCount = Interlocked.Increment(ref _coalescedControlUiInspectionRequests);
                if (ShouldLogInspectionInstrumentationCount(coalescedCount))
                {
                    _logger.Info("webview.inspect.coalesced", new
                    {
                        requested = TotalRequests,
                        coalesced = coalescedCount
                    });
                }

                return _inFlightInspectionTask;
            }

            if (_latestControlUiSnapshot != ControlUiProbeSnapshot.Unknown &&
                _latestControlUiSnapshotGeneration == generation &&
                DateTimeOffset.UtcNow - _lastControlUiInspectionAt < InspectionReuseWindow)
            {
                var cachedCount = Interlocked.Increment(ref _cachedControlUiInspectionRequests);
                if (ShouldLogInspectionInstrumentationCount(cachedCount))
                {
                    _logger.Info("webview.inspect.cached", new
                    {
                        requested = TotalRequests,
                        cached = cachedCount
                    });
                }

                return Task.FromResult(_latestControlUiSnapshot);
            }

            var inspectionSource = new TaskCompletionSource<ControlUiProbeSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _inFlightInspectionTask = inspectionSource.Task;
            _inFlightInspectionGeneration = generation;
            _ = CompleteControlUiInspectionAsync(coreWebView, inspectionSource, cancellationToken, generation);
            return inspectionSource.Task;
        }
    }

    private async Task ProbeControlUiStateAfterNavigationAsync(CancellationToken cancellationToken, int generation)
    {
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

                var snapshot = await InspectAsync(cancellationToken, generation);
                if (snapshot.IsTerminal)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when navigation changes.
        }
    }

    private void ApplyControlUiSnapshot(
        ControlUiProbeSnapshot snapshot,
        int generation,
        bool notifySnapshotUpdated)
    {
        var shouldNotify = notifySnapshotUpdated &&
            !EqualityComparer<ControlUiProbeSnapshot>.Default.Equals(_latestControlUiSnapshot, snapshot);
        _latestControlUiSnapshot = snapshot;
        _latestControlUiSnapshotGeneration = generation;

        if (snapshot.IsTerminal)
        {
            CancelProbeLoop();
        }

        if (shouldNotify)
        {
            SnapshotUpdated?.Invoke(snapshot);
        }
    }

    private async Task CompleteControlUiInspectionAsync(
        CoreWebView2 coreWebView,
        TaskCompletionSource<ControlUiProbeSnapshot> inspectionSource,
        CancellationToken cancellationToken,
        int generation)
    {
        try
        {
            inspectionSource.TrySetResult(await ExecuteControlUiInspectionAsync(
                coreWebView,
                cancellationToken,
                generation));
        }
        catch (OperationCanceledException)
        {
            inspectionSource.TrySetResult(ControlUiProbeSnapshot.Unknown);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to inspect hosted UI state: {ex.Message}");
            inspectionSource.TrySetResult(ControlUiProbeSnapshot.Unavailable(ex.Message));
        }
        finally
        {
            lock (_inspectionGate)
            {
                if (ReferenceEquals(_inFlightInspectionTask, inspectionSource.Task))
                {
                    _inFlightInspectionTask = null;
                }
            }
        }
    }

    private static bool ShouldLogInspectionInstrumentationCount(int count)
    {
        return count == 1 || count % 25 == 0;
    }

    private async Task<ControlUiProbeSnapshot> ExecuteControlUiInspectionAsync(
        CoreWebView2 coreWebView,
        CancellationToken cancellationToken,
        int generation)
    {
        const string script = """
(() => {
  if (!window.__openClawHostBridge || typeof window.__openClawHostBridge.inspect !== 'function') {
    return JSON.stringify({
      kind: 'openclaw-control-ui-status',
      phase: 'unavailable',
      summary: 'Control UI bridge unavailable.',
      detail: '',
      url: window.location ? window.location.href : '',
      shellDetected: false
    });
  }

  return JSON.stringify(window.__openClawHostBridge.inspect());
})()
""";

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_generations.IsCurrent(generation))
            {
                return ControlUiProbeSnapshot.Unknown;
            }

            var rawResult = await coreWebView.ExecuteScriptAsync(script);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_generations.IsCurrent(generation))
            {
                return ControlUiProbeSnapshot.Unknown;
            }

            _lastControlUiInspectionAt = DateTimeOffset.UtcNow;

            var payload = JsonSerializer.Deserialize<string>(rawResult);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return _latestControlUiSnapshotGeneration == generation
                    ? _latestControlUiSnapshot
                    : ControlUiProbeSnapshot.Unknown;
            }

            var snapshot = ParseControlUiSnapshot(payload);
            if (_generations.IsCurrent(generation))
            {
                ApplyControlUiSnapshot(snapshot, generation, notifySnapshotUpdated: true);
            }

            return snapshot;
        }
        catch (OperationCanceledException)
        {
            return ControlUiProbeSnapshot.Unknown;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to inspect hosted UI state: {ex.Message}");
            _lastControlUiInspectionAt = DateTimeOffset.UtcNow;
            return ControlUiProbeSnapshot.Unavailable(ex.Message);
        }
    }

    private static ControlUiProbeSnapshot ParseControlUiSnapshot(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.String)
        {
            var nested = root.GetString();
            return string.IsNullOrWhiteSpace(nested)
                ? ControlUiProbeSnapshot.Unknown
                : ParseControlUiSnapshot(nested);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return ControlUiProbeSnapshot.Unknown;
        }

        var kind = GetString(root, "kind");
        if (!string.Equals(kind, ControlUiStatusMessageKind, StringComparison.Ordinal))
        {
            return ControlUiProbeSnapshot.Unknown;
        }

        var phase = ParsePhase(GetString(root, "phase"));
        var summary = GetString(root, "summary");
        var detail = GetString(root, "detail");
        var url = GetString(root, "url");
        var shellDetected = root.TryGetProperty("shellDetected", out var shellProperty) &&
            shellProperty.ValueKind is JsonValueKind.True or JsonValueKind.False &&
            shellProperty.GetBoolean();
        var isBusy = root.TryGetProperty("isBusy", out var busyProperty) &&
            busyProperty.ValueKind is JsonValueKind.True or JsonValueKind.False &&
            busyProperty.GetBoolean();
        var inputFocused = root.TryGetProperty("inputFocused", out var inputFocusedProperty) &&
            inputFocusedProperty.ValueKind is JsonValueKind.True or JsonValueKind.False &&
            inputFocusedProperty.GetBoolean();
        var focusedInputHasText = root.TryGetProperty("focusedInputHasText", out var focusedInputHasTextProperty) &&
            focusedInputHasTextProperty.ValueKind is JsonValueKind.True or JsonValueKind.False &&
            focusedInputHasTextProperty.GetBoolean();
        var isBusyStale = root.TryGetProperty("isBusyStale", out var staleProperty) &&
            staleProperty.ValueKind is JsonValueKind.True or JsonValueKind.False &&
            staleProperty.GetBoolean();
        var busyStaleSeconds = root.TryGetProperty("busyStaleSeconds", out var staleSecondsProperty) &&
            staleSecondsProperty.ValueKind == JsonValueKind.Number &&
            staleSecondsProperty.TryGetInt32(out var parsedBusyStaleSeconds)
                ? parsedBusyStaleSeconds
                : 0;
        var workState = GetString(root, "workState");
        var currentModel = GetString(root, "currentModel");
        var currentModelSource = GetString(root, "currentModelSource");
        var activitySignature = GetString(root, "activitySignature");

        return new ControlUiProbeSnapshot(phase, summary, detail, url, shellDetected, isBusy, inputFocused, workState, currentModel)
        {
            FocusedInputHasText = focusedInputHasText,
            IsBusyStale = isBusyStale,
            BusyStaleSeconds = busyStaleSeconds,
            ActivitySignature = activitySignature,
            ModelSource = currentModelSource,
        };
    }

    private static string GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static ControlUiPhase ParsePhase(string value)
    {
        return value switch
        {
            "loading" => ControlUiPhase.Loading,
            "page_loaded" => ControlUiPhase.PageLoaded,
            "gateway_connecting" => ControlUiPhase.GatewayConnecting,
            "connected" => ControlUiPhase.Connected,
            "auth_required" => ControlUiPhase.AuthRequired,
            "pairing_required" => ControlUiPhase.PairingRequired,
            "origin_rejected" => ControlUiPhase.OriginRejected,
            "gateway_error" => ControlUiPhase.GatewayError,
            "unavailable" => ControlUiPhase.Unavailable,
            _ => ControlUiPhase.Unknown,
        };
    }
}
