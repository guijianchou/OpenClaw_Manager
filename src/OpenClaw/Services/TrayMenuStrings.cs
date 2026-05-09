// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

/// <summary>
/// Localized menu strings for the system tray context menu.
/// Injected at construction time so TrayIconService does not depend on WinUI resource infrastructure.
/// </summary>
public sealed record TrayMenuStrings(
    string OpenLabel,
    string ReloadLabel,
    string ViewLogsLabel,
    string CompactModeLabel,
    string SettingsLabel,
    string ExitLabel)
{
    /// <summary>
    /// English fallback strings used when no localized strings are provided.
    /// </summary>
    public static TrayMenuStrings Default { get; } = new(
        OpenLabel: "Open OpenClaw",
        ReloadLabel: "Reload",
        ViewLogsLabel: "View Logs",
        CompactModeLabel: "Compact Mode",
        SettingsLabel: "Settings",
        ExitLabel: "Exit");
}
