// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace OpenClaw.Services;

/// <summary>
/// Manages the JavaScript bridge between the hosted Control UI and the native shell.
/// Receives typed events from the page and sends commands back.
/// </summary>
public sealed class HostedUiBridge : IDisposable
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);

    private readonly IAppLogger _logger;
    private readonly WebViewMessageOwnership _messageOwnership;
    private WebView2? _webView;
    private CoreWebView2? _coreWebView;
    private TypedEventHandler<CoreWebView2, CoreWebView2WebMessageReceivedEventArgs>? _webMessageReceivedHandler;
    private string? _documentCreatedScriptId;
    private bool _isInitialized;
    private bool _isDisposed;
    private int _hostGeneration;

    internal HostedUiBridge(IAppLogger logger, WebViewMessageOwnership messageOwnership)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _messageOwnership = messageOwnership ?? throw new ArgumentNullException(nameof(messageOwnership));
    }

    /// <summary>
    /// Raised when the hosted UI reports session ready.
    /// </summary>
    public event Action<SessionReadyEventArgs>? SessionReady;

    /// <summary>
    /// Raised when an event gap is detected.
    /// </summary>
    public event Action<EventGapEventArgs>? EventGapDetected;

    /// <summary>
    /// Gets whether the bridge is initialized.
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// Gets the last known event sequence number.
    /// </summary>
    public long? LastKnownEventSeq { get; private set; }

    /// <summary>
    /// Gets the last known state version.
    /// </summary>
    public string? LastKnownStateVersion { get; private set; }

    /// <summary>
    /// Gets whether session ready has been emitted.
    /// </summary>
    public bool IsSessionReadyEmitted { get; private set; }

    /// <summary>
    /// Initializes the bridge by injecting the JavaScript payload.
    /// </summary>
    public async Task InitializeAsync(WebView2 webView, CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
        {
            return;
        }

        DetachCurrentWebView();
        _webView = webView;
        _coreWebView = null;
        var hostGeneration = _hostGeneration;

        try
        {
            var coreWebView = TryGetCoreWebView2(_webView);
            if (coreWebView is null)
            {
                throw new InvalidOperationException("Bridge cannot initialize before CoreWebView2 is available.");
            }

            _coreWebView = coreWebView;

            var scriptId = await coreWebView.AddScriptToExecuteOnDocumentCreatedAsync(
                HostedUiBridgeScript.Build(_messageOwnership.OwnerToken));
            if (cancellationToken.IsCancellationRequested)
            {
                RemoveDocumentCreatedScript(coreWebView, scriptId);
                throw new OperationCanceledException(cancellationToken);
            }

            if (_isDisposed ||
                !ReferenceEquals(webView, _webView) ||
                !IsCurrentHost(hostGeneration))
            {
                RemoveDocumentCreatedScript(coreWebView, scriptId);
                return;
            }

            _documentCreatedScriptId = scriptId;
            _webMessageReceivedHandler = CreateWebMessageReceivedHandler(hostGeneration);
            coreWebView.WebMessageReceived += _webMessageReceivedHandler;
            _isInitialized = true;
            _logger.Info("HostedUiBridge initialized.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.Info("HostedUiBridge initialization cancelled.");
        }
        catch (Exception ex)
        {
            _logger.Error($"HostedUiBridge initialization failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Sends a command to the hosted UI.
    /// </summary>
    public async Task<bool> SendCommandAsync(
        string command,
        object? payload = null,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        var coreWebView = GetCoreWebView();
        if (_isDisposed || coreWebView is null)
        {
            _logger.Warning($"HostedUiBridge command skipped before initialization: {command}");
            return false;
        }

        var pageVersion = _messageOwnership.CaptureAcceptedPageVersion();
        if (pageVersion == 0)
        {
            _logger.Warning($"HostedUiBridge command skipped before page ownership was accepted: {command}");
            return false;
        }

        try
        {
            var message = new { kind = "command", command, payload };
            var json = JsonSerializer.Serialize(message);
            var script = $"(async () => await window.__openClawHostBridge?.onCommand?.({json}) ?? false)()";
            using var timeout = new CancellationTokenSource(CommandTimeout);
            using var commandCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                timeout.Token,
                cancellationToken);
            var raw = await coreWebView.ExecuteScriptAsync(script)
                .AsTask(commandCancellation.Token);
            if (!IsStillCurrentCommandTarget(coreWebView, pageVersion))
            {
                return false;
            }

            return bool.TryParse(raw?.Trim('"'), out var handled) && handled;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.Info($"HostedUiBridge command '{command}' cancelled.");
            return false;
        }
        catch (OperationCanceledException)
        {
            _logger.Warning($"HostedUiBridge command '{command}' timed out after {CommandTimeout.TotalSeconds:0.#}s.");
            return false;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            _logger.Warning($"HostedUiBridge command '{command}' failed while WebView2 was unavailable: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Requests the hosted UI to refresh its session.
    /// </summary>
    public async Task<bool> RequestSessionRefreshAsync(CancellationToken cancellationToken = default)
    {
        return await SendCommandAsync("refresh_session", cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Requests the hosted UI to fetch recent messages.
    /// </summary>
    public async Task<bool> RequestRecentMessagesAsync(CancellationToken cancellationToken = default)
    {
        return await SendCommandAsync("fetch_recent_messages", cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Requests the hosted UI to perform a lightweight sync.
    /// </summary>
    public async Task<bool> RequestLightweightSyncAsync(CancellationToken cancellationToken = default)
    {
        return await SendCommandAsync("lightweight_sync", cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Notifies the hosted UI of reconnect intent.
    /// </summary>
    public async Task<bool> NotifyReconnectIntentAsync(CancellationToken cancellationToken = default)
    {
        return await SendCommandAsync("reconnect_intent", cancellationToken: cancellationToken);
    }

    private TypedEventHandler<CoreWebView2, CoreWebView2WebMessageReceivedEventArgs> CreateWebMessageReceivedHandler(int hostGeneration)
    {
        return (sender, args) => OnWebMessageReceived(sender, args, hostGeneration);
    }

    private void OnWebMessageReceived(
        CoreWebView2 sender,
        CoreWebView2WebMessageReceivedEventArgs args,
        int hostGeneration)
    {
        try
        {
            if (!IsCurrentHost(hostGeneration))
            {
                return;
            }

            var message = args.WebMessageAsJson;
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (!_messageOwnership.TryCaptureCurrentVersion(args, root, out var pageVersion))
            {
                return;
            }

            var kind = root.TryGetProperty("kind", out var kindProp) ? kindProp.GetString() : null;

            if (string.Equals(kind, "openclaw-session-ready", StringComparison.Ordinal))
            {
                var eventArgs = ParseSessionReadyEventArgs(root);
                if (!_messageOwnership.IsCurrentAcceptedPageVersion(pageVersion))
                {
                    return;
                }

                IsSessionReadyEmitted = true;
                SessionReady?.Invoke(eventArgs);
            }
            else if (string.Equals(kind, "openclaw-event-gap", StringComparison.Ordinal))
            {
                var eventArgs = ParseEventGapEventArgs(root);
                if (!_messageOwnership.IsCurrentAcceptedPageVersion(pageVersion))
                {
                    return;
                }

                LastKnownEventSeq = eventArgs.GotSeq;
                LastKnownStateVersion = eventArgs.CurrentStateVersion;
                EventGapDetected?.Invoke(eventArgs);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to process bridge message: {ex.Message}");
        }
    }

    private static SessionReadyEventArgs ParseSessionReadyEventArgs(JsonElement root)
    {
        var detectedAt = GetString(root, "detectedAt");
        var model = GetString(root, "model");
        var modelSource = GetString(root, "modelSource");
        var uri = GetString(root, "uri");

        return new SessionReadyEventArgs(detectedAt, model, uri, modelSource);
    }

    private static EventGapEventArgs ParseEventGapEventArgs(JsonElement root)
    {
        var expectedSeq = root.TryGetProperty("expectedSeq", out var prop) ? prop.GetInt64() : 0L;
        var gotSeq = root.TryGetProperty("gotSeq", out prop) ? prop.GetInt64() : 0L;
        var lastStateVersion = GetString(root, "lastStateVersion");
        var currentStateVersion = GetString(root, "currentStateVersion");
        var detectedAt = GetString(root, "detectedAt");

        return new EventGapEventArgs(expectedSeq, gotSeq, lastStateVersion, currentStateVersion, detectedAt);
    }

    private static string GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    /// <summary>
    /// Detaches from the current WebView2 control without disposing the bridge.
    /// </summary>
    public void DetachCurrentWebView()
    {
        _hostGeneration++;
        var coreWebView = GetCoreWebView();
        if (coreWebView is not null)
        {
            if (_webMessageReceivedHandler is not null)
            {
                coreWebView.WebMessageReceived -= _webMessageReceivedHandler;
            }

            RemoveDocumentCreatedScript(coreWebView);
        }
        else
        {
            _documentCreatedScriptId = null;
        }

        _webView = null;
        _coreWebView = null;
        _webMessageReceivedHandler = null;
        _isInitialized = false;
        LastKnownEventSeq = null;
        LastKnownStateVersion = null;
        IsSessionReadyEmitted = false;
    }

    /// <summary>
    /// Cleans up the bridge resources.
    /// </summary>
    public void Dispose()
    {
        _isDisposed = true;
        DetachCurrentWebView();
    }

    private static CoreWebView2? TryGetCoreWebView2(WebView2? webView)
    {
        if (webView is null)
        {
            return null;
        }

        try
        {
            return webView.CoreWebView2;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return null;
        }
    }

    private CoreWebView2? GetCoreWebView()
    {
        if (_coreWebView is not null)
        {
            return _coreWebView;
        }

        _coreWebView = TryGetCoreWebView2(_webView);
        return _coreWebView;
    }

    private bool IsStillCurrentCommandTarget(CoreWebView2 coreWebView, int pageVersion)
    {
        return !_isDisposed &&
            ReferenceEquals(coreWebView, _coreWebView) &&
            _messageOwnership.IsCurrentAcceptedPageVersion(pageVersion);
    }

    private bool IsCurrentHost(int hostGeneration)
    {
        return !_isDisposed &&
            _coreWebView is not null &&
            _hostGeneration == hostGeneration;
    }

    private void RemoveDocumentCreatedScript(CoreWebView2 coreWebView)
    {
        var scriptId = _documentCreatedScriptId;
        if (string.IsNullOrEmpty(scriptId))
        {
            return;
        }

        RemoveDocumentCreatedScript(coreWebView, scriptId);
    }

    private void RemoveDocumentCreatedScript(CoreWebView2 coreWebView, string scriptId)
    {
        try
        {
            coreWebView.RemoveScriptToExecuteOnDocumentCreated(scriptId);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            _logger.Warning($"HostedUiBridge script removal skipped while WebView2 was unavailable: {ex.Message}");
        }
        finally
        {
            if (string.Equals(_documentCreatedScriptId, scriptId, StringComparison.Ordinal))
            {
                _documentCreatedScriptId = null;
            }
        }
    }
}

/// <summary>
/// Event args for session ready event.
/// </summary>
