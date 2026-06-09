// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Diagnostics;

namespace OpenClaw.Services;

/// <summary>
/// Periodically pings the active Control UI host and publishes round-trip latency updates.
/// </summary>
public sealed class ControlUiLatencyService : IDisposable
{
    private const int ProbeIntervalSeconds = 3;

    private readonly HttpClient _probeHttpClient;
    private readonly IAppLogger _logger;
    private readonly TimeSpan _probeInterval;
    private readonly bool _disposeHttpClient;
    private readonly object _probeGate = new();
    private PeriodicTimer? _probeTimer;
    private CancellationTokenSource? _probeCts;
    private Task? _probeTask;
    private string? _currentHost;
    private string? _currentUrl;
    private ControlUiLatencySnapshot _lastSuccessSnapshot = ControlUiLatencySnapshot.Unknown;
    private ControlUiLatencySnapshot _lastPublishedSnapshot = ControlUiLatencySnapshot.Unknown;
    private int _probeRunId;

    public ControlUiLatencyService()
        : this(NullAppLogger.Instance)
    {
    }

    public ControlUiLatencyService(IAppLogger logger)
        : this(CreateProbeHttpClient(), TimeSpan.FromSeconds(ProbeIntervalSeconds), disposeHttpClient: true, logger)
    {
    }

    public ControlUiLatencyService(HttpMessageHandler messageHandler, TimeSpan? probeInterval = null, IAppLogger? logger = null)
        : this(new HttpClient(messageHandler) { Timeout = TimeSpan.FromSeconds(5) }, probeInterval ?? TimeSpan.FromSeconds(ProbeIntervalSeconds), disposeHttpClient: true, logger ?? NullAppLogger.Instance)
    {
    }

    private ControlUiLatencyService(
        HttpClient probeHttpClient,
        TimeSpan probeInterval,
        bool disposeHttpClient,
        IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(probeHttpClient);
        _probeHttpClient = probeHttpClient;
        _logger = logger;
        _probeInterval = probeInterval;
        _disposeHttpClient = disposeHttpClient;
    }

    /// <summary>
    /// Raised whenever a new latency snapshot is available.
    /// </summary>
    public event Action<ControlUiLatencySnapshot>? LatencyUpdated;

    /// <summary>
    /// Starts probing the supplied Control UI URL.
    /// </summary>
    public void Start(string? controlUiUrl)
    {
        var probeUri = ControlUiProbeUriFactory.TryCreateConfigUri(controlUiUrl);
        var probeKey = ControlUiProbeUriFactory.TryCreateProbeKey(probeUri);
        var host = TryGetProbeHost(probeUri);
        lock (_probeGate)
        {
            if (_probeCts is not null &&
                !_probeCts.IsCancellationRequested &&
                string.Equals(_currentUrl, controlUiUrl, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_currentHost, host, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        Stop();
        var runId = Interlocked.Increment(ref _probeRunId);

        lock (_probeGate)
        {
            _currentUrl = controlUiUrl;
            _currentHost = host;
            _lastSuccessSnapshot = ControlUiLatencySnapshot.Unknown;
            _lastPublishedSnapshot = ControlUiLatencySnapshot.Unknown;
        }

        if (probeUri is null || string.IsNullOrWhiteSpace(host))
        {
            _logger.Warning("control_ui.latency.disabled", new { controlUiUrl });
            PublishIfChanged(ControlUiLatencySnapshot.Unknown, runId);
            return;
        }

        _logger.Info("control_ui.latency.start", new { host, probeUri });
        var cancellation = new CancellationTokenSource();
        var timer = new PeriodicTimer(_probeInterval);

        lock (_probeGate)
        {
            _probeCts = cancellation;
            _probeTimer = timer;
            _probeTask = Task.Run(() => RunProbeLoopAsync(probeUri, probeKey, host, timer, cancellation, runId));
        }
    }

    /// <summary>
    /// Stops probing and releases timers.
    /// </summary>
    public void Stop()
    {
        CancellationTokenSource? cancellation;
        PeriodicTimer? timer;
        Task? task;

        lock (_probeGate)
        {
            _probeRunId++;
            _currentUrl = null;
            _currentHost = null;
            _lastSuccessSnapshot = ControlUiLatencySnapshot.Unknown;
            _lastPublishedSnapshot = ControlUiLatencySnapshot.Unknown;

            cancellation = _probeCts;
            timer = _probeTimer;
            task = _probeTask;
            _probeCts = null;
            _probeTimer = null;
            _probeTask = null;
        }

        if (cancellation is null)
        {
            timer?.Dispose();
            return;
        }

        cancellation.Cancel();
        if (task is null)
        {
            DisposeProbeResources(cancellation, timer);
            return;
        }

        _ = ObserveStopAsync(task);
    }

    public void Dispose()
    {
        Stop();
        if (_disposeHttpClient)
        {
            _probeHttpClient.Dispose();
        }
    }

    private async Task RunProbeLoopAsync(
        Uri probeUri,
        string? probeKey,
        string host,
        PeriodicTimer timer,
        CancellationTokenSource cancellation,
        int runId)
    {
        var cancellationToken = cancellation.Token;
        try
        {
            await PublishLatencyAsync(probeUri, probeKey, host, cancellationToken, runId).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested || !IsCurrentProbeRun(runId))
            {
                return;
            }

            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await PublishLatencyAsync(probeUri, probeKey, host, cancellationToken, runId).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _logger.Warning($"Control UI latency probe loop failed: {ex.Message}");
        }
        finally
        {
            lock (_probeGate)
            {
                if (ReferenceEquals(_probeCts, cancellation))
                {
                    _probeCts = null;
                    _probeTimer = null;
                    _probeTask = null;
                }
            }

            DisposeProbeResources(cancellation, timer);
        }
    }

    private async Task ObserveStopAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _logger.Warning($"Control UI latency probe shutdown failed: {ex.Message}");
        }
    }

    private static void DisposeProbeResources(CancellationTokenSource cancellation, PeriodicTimer? timer)
    {
        timer?.Dispose();
        cancellation.Dispose();
    }

    private static HttpClient CreateProbeHttpClient()
    {
        return new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
        })
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
    }

    private async Task PublishLatencyAsync(
        Uri probeUri,
        string? probeKey,
        string host,
        CancellationToken cancellationToken,
        int runId)
    {
        var snapshot = await ProbeAsync(probeUri, probeKey, host, cancellationToken).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested || !IsCurrentProbeRun(runId))
        {
            return;
        }

        if (snapshot.IsSuccess)
        {
            lock (_probeGate)
            {
                if (!IsCurrentProbeRunLocked(runId))
                {
                    return;
                }

                _lastSuccessSnapshot = snapshot;
            }

            PublishIfChanged(snapshot, runId);
            return;
        }

        ControlUiLatencySnapshot lastSuccessSnapshot;
        lock (_probeGate)
        {
            if (!IsCurrentProbeRunLocked(runId))
            {
                return;
            }

            lastSuccessSnapshot = _lastSuccessSnapshot;
        }

        if (lastSuccessSnapshot.IsSuccess)
        {
            PublishIfChanged(lastSuccessSnapshot with
            {
                State = ControlUiLatencyState.Stale,
                Detail = snapshot.Detail,
            }, runId);
            return;
        }

        PublishIfChanged(snapshot, runId);
    }

    private void PublishIfChanged(ControlUiLatencySnapshot snapshot, int runId)
    {
        Action<ControlUiLatencySnapshot>? latencyUpdated;
        lock (_probeGate)
        {
            if (!IsCurrentProbeRunLocked(runId))
            {
                return;
            }

            if (_lastPublishedSnapshot.Equals(snapshot))
            {
                return;
            }

            _lastPublishedSnapshot = snapshot;
            latencyUpdated = LatencyUpdated;
        }

        LogPublishedSnapshot(snapshot);
        latencyUpdated?.Invoke(snapshot);
    }

    private void LogPublishedSnapshot(ControlUiLatencySnapshot snapshot)
    {
        switch (snapshot.State)
        {
            case ControlUiLatencyState.Success:
                _logger.Info("control_ui.latency.success", new
                {
                    snapshot.Host,
                    snapshot.RoundtripTimeMs,
                    snapshot.Detail,
                    snapshot.ProxyPoP
                });
                break;
            case ControlUiLatencyState.Stale:
                _logger.Warning("control_ui.latency.stale", new
                {
                    snapshot.Host,
                    snapshot.RoundtripTimeMs,
                    snapshot.Detail,
                    snapshot.ProxyPoP
                });
                break;
            case ControlUiLatencyState.Failure:
                _logger.Warning("control_ui.latency.failure", new
                {
                    snapshot.Host,
                    snapshot.Detail
                });
                break;
            default:
                break;
        }
    }

    private bool IsCurrentProbeRun(int runId)
    {
        lock (_probeGate)
        {
            return IsCurrentProbeRunLocked(runId);
        }
    }

    private bool IsCurrentProbeRunLocked(int runId)
    {
        return _probeRunId == runId;
    }

    private async Task<ControlUiLatencySnapshot> ProbeAsync(
        Uri probeUri,
        string? probeKey,
        string host,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, probeUri);
            request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache, no-store, max-age=0");
            request.Headers.TryAddWithoutValidation("Pragma", "no-cache");
            request.Headers.TryAddWithoutValidation("Accept", "application/json,text/plain,*/*");

            var stopwatch = Stopwatch.StartNew();
            using var response = await _probeHttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            var viaCloudflare = response.Headers.TryGetValues("cf-ray", out var cfRayValues);
            var proxyPoP = cfRayValues is not null ? CloudflareRayParser.ParsePoP(cfRayValues.FirstOrDefault()) : null;
            var classification = await GatewayHttpStatusClassifier.ClassifyResponseAsync(response, cancellationToken).ConfigureAwait(false);
            var detail = $"{classification.Detail} {stopwatch.ElapsedMilliseconds} ms";
            if (classification.Kind != GatewayHttpStatusKind.Reachable)
            {
                return ControlUiLatencySnapshot.Failure(host, detail, proxyPoP, probeKey);
            }

            return ControlUiLatencySnapshot.Success(
                host,
                stopwatch.ElapsedMilliseconds,
                detail,
                proxyPoP,
                probeKey);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ControlUiLatencySnapshot.Failure(host, ex.Message, probeKey: probeKey);
        }
    }

    private static string? TryGetProbeHost(Uri? uri)
    {
        if (uri is null)
        {
            return null;
        }

        var host = string.IsNullOrWhiteSpace(uri.IdnHost)
            ? uri.Host
            : uri.IdnHost;

        return string.IsNullOrWhiteSpace(host)
            ? null
            : host.Trim('[', ']');
    }
}

public enum ControlUiLatencyState
{
    Unknown,
    Success,
    Stale,
    Failure,
}

/// <summary>
/// Represents the latest latency probe result for the Control UI host.
/// </summary>
public readonly record struct ControlUiLatencySnapshot(
    ControlUiLatencyState State,
    string Host,
    long? RoundtripTimeMs,
    string? Detail = null,
    string? ProxyPoP = null,
    string ProbeKey = "")
{
    public static ControlUiLatencySnapshot Unknown => new(ControlUiLatencyState.Unknown, string.Empty, null);

    public static ControlUiLatencySnapshot Success(
        string host,
        long roundtripTimeMs,
        string? detail = null,
        string? proxyPoP = null,
        string? probeKey = null) =>
        new(ControlUiLatencyState.Success, host, roundtripTimeMs, detail, proxyPoP, probeKey ?? string.Empty);

    public static ControlUiLatencySnapshot Failure(
        string host,
        string? detail = null,
        string? proxyPoP = null,
        string? probeKey = null) =>
        new(ControlUiLatencyState.Failure, host, null, detail, proxyPoP, probeKey ?? string.Empty);

    public bool IsSuccess => State == ControlUiLatencyState.Success && RoundtripTimeMs is not null;
}
