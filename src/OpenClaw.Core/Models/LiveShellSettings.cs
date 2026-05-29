// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Models;

public sealed record LiveShellSettings(
    bool AlwaysOnTop,
    bool EnableGlobalHotkey,
    string GlobalHotkey,
    bool AllowMultipleInstances)
{
    public static LiveShellSettings From(AppSettings settings)
    {
        return new LiveShellSettings(
            settings.AlwaysOnTop,
            settings.EnableGlobalHotkey,
            settings.GlobalHotkey.Trim(),
            settings.AllowMultipleInstances);
    }
}
