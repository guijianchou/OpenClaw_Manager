// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Globalization;
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
            return;
        }

        _globalHotkeyService = new GlobalHotkeyService(App.Logger);
        var result = _globalHotkeyService.Register(binding);
        if (result.Succeeded)
        {
            _globalHotkeyService.HotkeyPressed += OnGlobalHotkeyPressed;
        }
        else
        {
            _globalHotkeyService.Dispose();
            _globalHotkeyService = null;
            ShowGlobalHotkeyRegistrationFailure(result);
        }
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

    private void ShowGlobalHotkeyRegistrationFailure(GlobalHotkeyRegistrationResult result)
    {
        var binding = result.Binding?.ToString() ?? App.Configuration.Settings.GlobalHotkey;
        var errorCode = result.ErrorCode?.ToString(CultureInfo.InvariantCulture) ?? "n/a";
        ViewModel.ShowGlobalHotkeyRegistrationError(
            string.Format(StringResources.GlobalHotkeyRegistrationFailedFormat, binding, errorCode));
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
