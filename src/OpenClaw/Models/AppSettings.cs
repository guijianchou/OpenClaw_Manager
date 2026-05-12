// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Text.Json.Serialization;

namespace OpenClaw.Models;

/// <summary>
/// Application-level settings persisted as JSON to local storage.
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Gets or sets the list of configured gateway environments.
    /// </summary>
    public List<EnvironmentConfig> Environments { get; set; } = [];

    /// <summary>
    /// Gets or sets the name of the currently selected environment.
    /// </summary>
    public string? SelectedEnvironmentName { get; set; }

    /// <summary>
    /// Gets or sets the remembered main window width.
    /// </summary>
    public double WindowWidth { get; set; } = 1280;

    /// <summary>
    /// Gets or sets the remembered main window height.
    /// </summary>
    public double WindowHeight { get; set; } = 800;

    /// <summary>
    /// Gets or sets the remembered main window left position.
    /// </summary>
    public double WindowLeft { get; set; } = -1;

    /// <summary>
    /// Gets or sets the remembered main window top position.
    /// </summary>
    public double WindowTop { get; set; } = -1;

    /// <summary>
    /// Gets or sets the preferred application theme (System, Light, Dark).
    /// </summary>
    public string AppTheme { get; set; } = "System";

    /// <summary>
    /// Gets or sets the preferred application language (System, en-US, zh-CN).
    /// </summary>
    public string AppLanguage { get; set; } = "en-US";

    /// <summary>
    /// Gets or sets whether minimizing the main window hides it to the system tray.
    /// </summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>
    /// Gets or sets whether closing the main window hides it to the system tray.
    /// </summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>
    /// Gets or sets whether OpenClaw allows multiple app instances on Windows.
    /// </summary>
    public bool AllowMultipleInstances { get; set; } = false;

    /// <summary>
    /// Gets or sets the global hotkey binding string (e.g. "Ctrl+Alt+Space").
    /// Empty or null disables the hotkey.
    /// </summary>
    public string GlobalHotkey { get; set; } = "Ctrl+Alt+Space";

    /// <summary>
    /// Gets or sets whether the global hotkey is enabled.
    /// </summary>
    public bool EnableGlobalHotkey { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the main window stays on top of other windows.
    /// </summary>
    public bool AlwaysOnTop { get; set; } = false;

    /// <summary>
    /// Gets or sets whether compact mode is active (reduced window showing only status).
    /// </summary>
    public bool CompactMode { get; set; } = false;

    /// <summary>
    /// Gets or sets the compact mode window left position.
    /// </summary>
    public double CompactWindowLeft { get; set; } = -1;

    /// <summary>
    /// Gets or sets the compact mode window top position.
    /// </summary>
    public double CompactWindowTop { get; set; } = -1;

    /// <summary>
    /// Gets or sets the heartbeat probe interval in seconds. 0 = disabled.
    /// Default is 30s, which works well with Cloudflare Tunnel / reverse proxy idle timeouts (60-100s).
    /// </summary>
    public int HeartbeatIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the connection recovery policy options.
    /// </summary>
    public RecoveryPolicyOptions RecoveryPolicy { get; set; } = new();

    /// <summary>
    /// Gets or sets the heartbeat options.
    /// </summary>
    public HeartbeatOptions Heartbeat { get; set; } = new();

    /// <summary>
    /// Gets or sets the diagnostics options.
    /// </summary>
    public DiagnosticsOptions Diagnostics { get; set; } = new();
}

/// <summary>
/// Source generation context for System.Text.Json serialization.
/// Enables AOT-friendly JSON serialization for AppSettings.
/// </summary>
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(RecoveryPolicyOptions))]
[JsonSerializable(typeof(HeartbeatOptions))]
[JsonSerializable(typeof(DiagnosticsOptions))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class AppSettingsJsonContext : JsonSerializerContext
{
}
