// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Xaml;
using OpenClaw.Helpers;
using Windows.Graphics;

namespace OpenClaw.Views;

public sealed partial class SettingsDialog
{
    private void ConfigureWindowChrome()
    {
        Title = Helpers.StringResources.SettingsTitle;
        AppWindow.SetIcon("Assets\\WindowIcon.ico");
        RestoreSettingsWindowBounds();
        this.Closed += OnSettingsDialogClosed;
        this.Activated += OnWindowActivated;
    }

    private void AttachRootEventHandlers()
    {
        if (this.Content is not FrameworkElement rootElement)
        {
            return;
        }

        rootElement.Loaded += OnRootLoaded;
        rootElement.ActualThemeChanged += OnRootActualThemeChanged;
        ApplyTheme(App.Configuration.Settings.AppTheme);
    }

    private void InitializeEnvironmentBindings()
    {
        EnvironmentList.ItemsSource = ViewModel.Environments;
        SelectFirstEnvironment();
    }

    private void InitializeNavigationState()
    {
        PopulateLanguageOptions();
        NavList.SelectedIndex = 0;
    }

    private void RestoreSettingsWindowBounds()
    {
        _ = TryRestoreSettingsWindowBounds(logOffscreenRecovery: false);
    }

    private void RestoreSettingsWindowBoundsAfterActivation()
    {
        if (_hasRestoredSettingsBoundsAfterActivation)
        {
            return;
        }

        _hasRestoredSettingsBoundsAfterActivation = true;
        _ = TryRestoreSettingsWindowBounds(logOffscreenRecovery: true);
    }

    private bool TryRestoreSettingsWindowBounds(bool logOffscreenRecovery)
    {
        var settings = App.Configuration.Settings;
        var width = (int)settings.SettingsWindowWidth;
        var height = (int)settings.SettingsWindowHeight;

        if (!WindowBoundsUtilities.HasPersistableSize(
                width,
                height,
                WindowBoundsUtilities.MinimumPersistedSettingsWindowWidth,
                WindowBoundsUtilities.MinimumPersistedSettingsWindowHeight))
        {
            width = WindowBoundsUtilities.DefaultSettingsWindowWidth;
            height = WindowBoundsUtilities.DefaultSettingsWindowHeight;
        }

        if (!WindowBoundsUtilities.HasSavedPosition(settings.SettingsWindowLeft, settings.SettingsWindowTop))
        {
            AppWindow.Resize(new SizeInt32(width, height));
            return false;
        }

        var left = (int)settings.SettingsWindowLeft;
        var top = (int)settings.SettingsWindowTop;
        if (WindowFrameHelper.IsNativeWindowRectVisibleWithinAnyMonitor(left, top, width, height))
        {
            if (!WindowFrameHelper.TrySetWindowRect(this, left, top, width, height))
            {
                AppWindow.Resize(new SizeInt32(width, height));
                AppWindow.Move(new PointInt32(left, top));
            }

            return true;
        }

        if (WindowFrameHelper.TryCenterNativeWindowRectInNearestMonitor(
                left,
                top,
                width,
                height,
                out var centeredLeft,
                out var centeredTop))
        {
            if (!WindowFrameHelper.TrySetWindowRect(this, centeredLeft, centeredTop, width, height))
            {
                AppWindow.Resize(new SizeInt32(width, height));
                AppWindow.Move(new PointInt32(centeredLeft, centeredTop));
            }

            if (logOffscreenRecovery)
            {
                App.Logger.Info("Saved Settings window bounds were outside current displays; moved window to the current display.");
            }

            return true;
        }

        AppWindow.Resize(new SizeInt32(width, height));
        return false;
    }

    private void OnSettingsDialogClosed(object sender, WindowEventArgs args)
    {
        SaveSettingsWindowBounds();
        this.Closed -= OnSettingsDialogClosed;
        this.Activated -= OnWindowActivated;
    }

    private void SaveSettingsWindowBounds()
    {
        if (WindowFrameHelper.IsWindowMinimized(this))
        {
            App.Logger.Info("Skipping Settings window bounds save because the window is minimized.");
            return;
        }

        try
        {
            if (!WindowFrameHelper.TryGetWindowRect(this, out var left, out var top, out var width, out var height))
            {
                App.Logger.Warning("Skipping Settings window bounds save because native window bounds were unavailable.");
                return;
            }

            if (!WindowBoundsUtilities.CanPersistWindowBounds(
                    left,
                    top,
                    width,
                    height,
                    WindowBoundsUtilities.MinimumPersistedSettingsWindowWidth,
                    WindowBoundsUtilities.MinimumPersistedSettingsWindowHeight))
            {
                App.Logger.Warning($"Skipping invalid Settings window bounds: x={left}, y={top}, width={width}, height={height}");
                return;
            }

            App.Configuration.Settings.SettingsWindowWidth = width;
            App.Configuration.Settings.SettingsWindowHeight = height;
            App.Configuration.Settings.SettingsWindowLeft = left;
            App.Configuration.Settings.SettingsWindowTop = top;
            App.Configuration.SaveDeferred();
        }
        catch (Exception ex)
        {
            App.Logger.Warning($"Failed to save Settings window bounds: {ex.Message}");
        }
    }
}
