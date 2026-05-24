// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Models;

public sealed record LiveShellSettingsChange(
    LiveShellSettings Before,
    LiveShellSettings After)
{
    public bool DidChangeAlwaysOnTop => Before.AlwaysOnTop != After.AlwaysOnTop;

    public bool DidChangeGlobalHotkey =>
        Before.EnableGlobalHotkey != After.EnableGlobalHotkey ||
        !string.Equals(Before.GlobalHotkey, After.GlobalHotkey, StringComparison.Ordinal);

    public bool HasChanges => DidChangeAlwaysOnTop || DidChangeGlobalHotkey;
}
