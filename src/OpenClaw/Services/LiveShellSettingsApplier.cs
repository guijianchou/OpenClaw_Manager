// Copyright (c) Lanstack @openclaw. All rights reserved.

using OpenClaw.Models;

namespace OpenClaw.Services;

internal sealed class LiveShellSettingsApplier
{
    private readonly Action<bool> _setAlwaysOnTop;
    private readonly Action _reapplyGlobalHotkey;

    public LiveShellSettingsApplier(Action<bool> setAlwaysOnTop, Action reapplyGlobalHotkey)
    {
        _setAlwaysOnTop = setAlwaysOnTop;
        _reapplyGlobalHotkey = reapplyGlobalHotkey;
    }

    public void Apply(LiveShellSettingsChange change)
    {
        if (change.DidChangeAlwaysOnTop)
        {
            _setAlwaysOnTop(change.After.AlwaysOnTop);
        }

        if (change.DidChangeGlobalHotkey)
        {
            _reapplyGlobalHotkey();
        }
    }
}
