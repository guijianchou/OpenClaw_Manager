// Copyright (c) Lanstack @openclaw. All rights reserved.

using OpenClaw.Helpers;
using OpenClaw.Services;

namespace OpenClaw;

public sealed partial class MainWindow
{
    private void InitializeGlobalHotkey()
    {
        var settings = App.Configuration.Settings;
        if (!settings.EnableGlobalHotkey)
        {
            App.Logger.Info("Global hotkey is disabled in settings.");
            return;
        }

        var binding = HotkeyBinding.Parse(settings.GlobalHotkey);
        if (binding is null)
        {
            App.Logger.Warning($"Global hotkey binding could not be parsed: '{settings.GlobalHotkey}'");
            NotifyGlobalHotkeyFailed(settings.GlobalHotkey);
            return;
        }

        _globalHotkeyService = new GlobalHotkeyService(App.Logger);
        if (_globalHotkeyService.TryRegister(binding))
        {
            _globalHotkeyService.HotkeyPressed += OnGlobalHotkeyPressed;
        }
        else
        {
            _globalHotkeyService.Dispose();
            _globalHotkeyService = null;
            NotifyGlobalHotkeyFailed(settings.GlobalHotkey);
        }
    }

    private void NotifyGlobalHotkeyFailed(string hotkey)
    {
        ViewModel.ShowShellWarning(
            string.Format(StringResources.GlobalHotkeyRegistrationFailedFormat, hotkey));
    }

    private void DisposeGlobalHotkey()
    {
        if (_globalHotkeyService is null)
        {
            return;
        }

        _globalHotkeyService.HotkeyPressed -= OnGlobalHotkeyPressed;
        _globalHotkeyService.Dispose();
        _globalHotkeyService = null;
    }

    private void ReapplyGlobalHotkey()
    {
        DisposeGlobalHotkey();
        InitializeGlobalHotkey();
    }

    private void OnGlobalHotkeyPressed()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isWindowHidden || WindowFrameHelper.IsWindowMinimized(this))
            {
                ShowMainWindowFromTray();
            }
            else
            {
                HideMainWindowToTray();
            }
        });
    }
}
