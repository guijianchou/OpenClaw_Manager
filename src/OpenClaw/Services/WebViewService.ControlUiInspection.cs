// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace OpenClaw.Services;

public partial class WebViewService
{
    private const string ControlUiStatusMessageKind = "openclaw-control-ui-status";

    private CancellationTokenSource? _statusProbeCts;
    private readonly object _inspectionGate = new();
    private Task<ControlUiProbeSnapshot>? _inFlightInspectionTask;
    private int _inFlightInspectionGeneration;
    private DateTimeOffset _lastControlUiInspectionAt = DateTimeOffset.MinValue;
    private ControlUiProbeSnapshot _latestControlUiSnapshot = ControlUiProbeSnapshot.Unknown;
    private string? _lastReportedIssueKey;
    private static readonly TimeSpan InspectionReuseWindow = TimeSpan.FromMilliseconds(350);
    private int _totalControlUiInspectionRequests;
    private int _cachedControlUiInspectionRequests;
    private int _coalescedControlUiInspectionRequests;

    /// <summary>
    /// Raised when the hosted Control UI reports an updated snapshot.
    /// </summary>
    public event Action<ControlUiProbeSnapshot>? ControlUiSnapshotUpdated;

    /// <summary>
    /// Gets the latest control UI probe snapshot observed from the hosted page.
    /// </summary>
    public ControlUiProbeSnapshot LatestControlUiSnapshot => _latestControlUiSnapshot;

    public int TotalControlUiInspectionRequests => Volatile.Read(ref _totalControlUiInspectionRequests);

    public int CachedControlUiInspectionRequests => Volatile.Read(ref _cachedControlUiInspectionRequests);

    public int CoalescedControlUiInspectionRequests => Volatile.Read(ref _coalescedControlUiInspectionRequests);

    /// <summary>
    /// Attempts to inspect the hosted Control UI state via the injected page bridge.
    /// </summary>
    public Task<ControlUiProbeSnapshot> InspectControlUiStateAsync()
    {
        return InspectControlUiStateAsync(CancellationToken.None, Volatile.Read(ref _webViewGeneration));
    }

    private Task<ControlUiProbeSnapshot> InspectControlUiStateAsync(CancellationToken token, int generation)
    {
        Interlocked.Increment(ref _totalControlUiInspectionRequests);
        if (token.IsCancellationRequested || !IsCurrentGeneration(generation))
        {
            return Task.FromResult(ControlUiProbeSnapshot.Unknown);
        }

        var coreWebView = GetCoreWebView();
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
                    App.Logger.Info("webview.inspect.coalesced", new
                    {
                        requested = TotalControlUiInspectionRequests,
                        coalesced = coalescedCount
                    });
                }
                return _inFlightInspectionTask;
            }

            if (_latestControlUiSnapshot != ControlUiProbeSnapshot.Unknown &&
                DateTimeOffset.UtcNow - _lastControlUiInspectionAt < InspectionReuseWindow)
            {
                var cachedCount = Interlocked.Increment(ref _cachedControlUiInspectionRequests);
                if (ShouldLogInspectionInstrumentationCount(cachedCount))
                {
                    App.Logger.Info("webview.inspect.cached", new
                    {
                        requested = TotalControlUiInspectionRequests,
                        cached = cachedCount
                    });
                }
                return Task.FromResult(_latestControlUiSnapshot);
            }

            var inspectionSource = new TaskCompletionSource<ControlUiProbeSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            _inFlightInspectionTask = inspectionSource.Task;
            _inFlightInspectionGeneration = generation;
            _ = CompleteControlUiInspectionAsync(coreWebView, inspectionSource, token, generation);
            return inspectionSource.Task;
        }
    }

    private void StartStatusProbeLoop()
    {
        CancelStatusProbeLoop();
        _statusProbeCts = new CancellationTokenSource();
        var generation = Volatile.Read(ref _webViewGeneration);
        _ = ProbeControlUiStateAfterNavigationAsync(_statusProbeCts.Token, generation);
    }

    private void CancelStatusProbeLoop()
    {
        if (_statusProbeCts is not null)
        {
            _statusProbeCts.Cancel();
            _statusProbeCts.Dispose();
            _statusProbeCts = null;
        }
    }

    private async Task ProbeControlUiStateAfterNavigationAsync(CancellationToken token, int generation)
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
                await Task.Delay(delay, token);
                if (!IsCurrentGeneration(generation))
                {
                    return;
                }

                var snapshot = await InspectControlUiStateAsync(token, generation);
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

    private void ApplyControlUiSnapshot(ControlUiProbeSnapshot snapshot, bool raiseIssueEvent)
    {
        var notifySnapshotUpdated = !EqualityComparer<ControlUiProbeSnapshot>.Default.Equals(_latestControlUiSnapshot, snapshot);
        _latestControlUiSnapshot = snapshot;

        if (snapshot.IsTerminal)
        {
            CancelStatusProbeLoop();
        }

        if (notifySnapshotUpdated)
        {
            ControlUiSnapshotUpdated?.Invoke(snapshot);
        }

        switch (snapshot.Phase)
        {
            case ControlUiPhase.Loading:
                SetState(ConnectionState.Loading);
                break;
            case ControlUiPhase.PageLoaded:
            case ControlUiPhase.GatewayConnecting:
                SetState(ConnectionState.GatewayConnecting);
                break;
            case ControlUiPhase.Connected:
                _lastReportedIssueKey = null;
                SetState(ConnectionState.Connected);
                break;
            case ControlUiPhase.AuthRequired:
                SetState(ConnectionState.AuthFailed);
                break;
            case ControlUiPhase.PairingRequired:
            case ControlUiPhase.OriginRejected:
            case ControlUiPhase.GatewayError:
                SetState(ConnectionState.Error);
                break;
            case ControlUiPhase.Unavailable:
            case ControlUiPhase.Unknown:
            default:
                break;
        }

        if (!raiseIssueEvent || !snapshot.IsIssue)
        {
            return;
        }

        if (string.Equals(snapshot.IssueKey, _lastReportedIssueKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastReportedIssueKey = snapshot.IssueKey;
        NavigationErrorOccurred?.Invoke(snapshot.DetailOrSummary);
    }

    private async Task CompleteControlUiInspectionAsync(
        CoreWebView2 coreWebView,
        TaskCompletionSource<ControlUiProbeSnapshot> inspectionSource,
        CancellationToken token,
        int generation)
    {
        try
        {
            inspectionSource.TrySetResult(await ExecuteControlUiInspectionAsync(coreWebView, token, generation));
        }
        catch (OperationCanceledException)
        {
            inspectionSource.TrySetResult(ControlUiProbeSnapshot.Unknown);
        }
        catch (Exception ex)
        {
            App.Logger.Warning($"Failed to inspect hosted UI state: {ex.Message}");
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

    private async Task<ControlUiProbeSnapshot> ExecuteControlUiInspectionAsync(CoreWebView2 coreWebView, CancellationToken token, int generation)
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
            token.ThrowIfCancellationRequested();
            if (!IsCurrentGeneration(generation))
            {
                return ControlUiProbeSnapshot.Unknown;
            }

            var rawResult = await coreWebView.ExecuteScriptAsync(script);
            token.ThrowIfCancellationRequested();
            if (!IsCurrentGeneration(generation))
            {
                return ControlUiProbeSnapshot.Unknown;
            }

            _lastControlUiInspectionAt = DateTimeOffset.UtcNow;

            var payload = JsonSerializer.Deserialize<string>(rawResult);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return _latestControlUiSnapshot;
            }

            var snapshot = ParseControlUiSnapshot(payload);
            if (IsCurrentGeneration(generation))
            {
                ApplyControlUiSnapshot(snapshot, raiseIssueEvent: false);
            }

            return snapshot;
        }
        catch (OperationCanceledException)
        {
            return ControlUiProbeSnapshot.Unknown;
        }
        catch (Exception ex)
        {
            App.Logger.Warning($"Failed to inspect hosted UI state: {ex.Message}");
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
