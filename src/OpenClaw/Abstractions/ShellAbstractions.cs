// Copyright (c) Lanstack @openclaw. All rights reserved.
// Phase 5: Upstream-compatible abstraction interfaces.
// These isolate the generic shell/hosting concepts from OpenClaw-specific logic,
// enabling potential reuse in OpenClaw Control or other WebView2-based shells.

using OpenClaw.Models;

namespace OpenClaw.Abstractions;

/// <summary>
/// Abstraction for a remote environment that the shell connects to.
/// Generic enough to be reused by any WebView2-hosting management shell.
/// </summary>
public interface IRemoteEnvironment
{
    string Name { get; }
    string Url { get; }
    bool IsDefault { get; }
}

/// <summary>
/// Abstraction for WebView2 hosting and lifecycle management.
/// Separates the generic browser-hosting concerns from app-specific logic.
/// </summary>
public interface IWebViewHost
{
    bool IsInitialized { get; }
    string? CurrentUrl { get; }

    void Navigate(string url);
    bool Reload();
    HostDevToolsOpenResult OpenDevTools();

    Task ClearBrowsingDataAsync();
    bool RetryNavigation();

    event Action<HostConnectionState>? ConnectionStateChanged;
    event Action<string>? NavigationErrorOccurred;
}

/// <summary>
/// Generic DevTools open result, decoupled from concrete WebView2 service types.
/// </summary>
public readonly record struct HostDevToolsOpenResult(HostDevToolsOpenStatus Status, string? Message = null)
{
    public bool Succeeded => Status == HostDevToolsOpenStatus.Opened;
}

public enum HostDevToolsOpenStatus
{
    Opened,
    Unavailable,
    Disabled,
    Failed,
}

/// <summary>
/// Generic connection state, decoupled from the OpenClaw-specific enum.
/// </summary>
public enum HostConnectionState
{
    Offline,
    Loading,
    Connected,
    Reconnecting,
    AuthFailed,
    Error,
}

/// <summary>
/// Abstraction for startup diagnostics.
/// </summary>
public interface IDiagnosticRunner
{
    Task<string> RunDiagnosticsAsync(string? targetUrl);
}

/// <summary>
/// Abstraction for configuration persistence.
/// </summary>
public interface IConfigurationStore<T> where T : class, new()
{
    T Settings { get; }
    void Load();
    SettingsWriteResult Save();
}
