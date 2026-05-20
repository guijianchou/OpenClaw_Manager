// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace OpenClaw.Services;

/// <summary>
/// Manages the JavaScript bridge between the hosted Control UI and the native shell.
/// Receives typed events from the page and sends commands back.
/// </summary>
public sealed class HostedUiBridge : IDisposable
{
    private static string? _bridgeScriptResource;

    private static string BridgeScriptResource => _bridgeScriptResource ??= HostedUiBridgeScript.Build();

    private WebView2? _webView;
    private CoreWebView2? _coreWebView;
    private bool _isInitialized;

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
    public async Task InitializeAsync(WebView2 webView)
    {
        var previousCoreWebView = GetCoreWebView();
        if (previousCoreWebView is not null)
        {
            previousCoreWebView.WebMessageReceived -= OnWebMessageReceived;
        }

        _webView = webView;
        _coreWebView = null;
        LastKnownEventSeq = null;
        LastKnownStateVersion = null;
        IsSessionReadyEmitted = false;

        try
        {
            var coreWebView = TryGetCoreWebView2(_webView);
            if (coreWebView is null)
            {
                throw new InvalidOperationException("Bridge cannot initialize before CoreWebView2 is available.");
            }

            _coreWebView = coreWebView;

            await coreWebView.AddScriptToExecuteOnDocumentCreatedAsync(BridgeScriptResource);
            coreWebView.WebMessageReceived += OnWebMessageReceived;
            _isInitialized = true;
            App.Logger.Info("HostedUiBridge initialized.");
        }
        catch (Exception ex)
        {
            App.Logger.Error($"HostedUiBridge initialization failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Sends a command to the hosted UI.
    /// </summary>
    public async Task<bool> SendCommandAsync(string command, object? payload = null)
    {
        var coreWebView = GetCoreWebView();
        if (coreWebView is null)
        {
            App.Logger.Warning($"HostedUiBridge command skipped before initialization: {command}");
            return false;
        }

        try
        {
            var message = new { kind = "command", command, payload };
            var json = JsonSerializer.Serialize(message);
            var script = $"(async () => await window.__openClawHostBridge?.onCommand?.({json}) ?? false)()";
            var raw = await coreWebView.ExecuteScriptAsync(script);
            return bool.TryParse(raw?.Trim('"'), out var handled) && handled;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            App.Logger.Warning($"HostedUiBridge command '{command}' failed while WebView2 was unavailable: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Requests the hosted UI to refresh its session.
    /// </summary>
    public async Task<bool> RequestSessionRefreshAsync()
    {
        return await SendCommandAsync("refresh_session");
    }

    /// <summary>
    /// Requests the hosted UI to fetch recent messages.
    /// </summary>
    public async Task<bool> RequestRecentMessagesAsync()
    {
        return await SendCommandAsync("fetch_recent_messages");
    }

    /// <summary>
    /// Requests the hosted UI to perform a lightweight sync.
    /// </summary>
    public async Task<bool> RequestLightweightSyncAsync()
    {
        return await SendCommandAsync("lightweight_sync");
    }

    /// <summary>
    /// Notifies the hosted UI of reconnect intent.
    /// </summary>
    public async Task<bool> NotifyReconnectIntentAsync()
    {
        return await SendCommandAsync("reconnect_intent");
    }

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            var message = args.WebMessageAsJson;
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;

            var kind = root.TryGetProperty("kind", out var kindProp) ? kindProp.GetString() : null;

            if (string.Equals(kind, "openclaw-session-ready", StringComparison.Ordinal))
            {
                IsSessionReadyEmitted = true;
                var eventArgs = ParseSessionReadyEventArgs(root);
                SessionReady?.Invoke(eventArgs);
            }
            else if (string.Equals(kind, "openclaw-event-gap", StringComparison.Ordinal))
            {
                var eventArgs = ParseEventGapEventArgs(root);
                LastKnownEventSeq = eventArgs.GotSeq;
                LastKnownStateVersion = eventArgs.CurrentStateVersion;
                EventGapDetected?.Invoke(eventArgs);
            }
        }
        catch (Exception ex)
        {
            App.Logger.Warning($"Failed to process bridge message: {ex.Message}");
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
    /// Cleans up the bridge resources.
    /// </summary>
    public void Dispose()
    {
        var coreWebView = GetCoreWebView();
        if (coreWebView is not null)
        {
            coreWebView.WebMessageReceived -= OnWebMessageReceived;
        }
        _webView = null;
        _coreWebView = null;
        _isInitialized = false;
        LastKnownEventSeq = null;
        LastKnownStateVersion = null;
        IsSessionReadyEmitted = false;
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
}

/// <summary>
/// Event args for session ready event.
/// </summary>
