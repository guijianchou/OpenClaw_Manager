// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using OpenClaw.Abstractions;
using OpenClaw.Services;
using OpenClaw.ViewModels;

namespace OpenClaw;

/// <summary>
/// The main application window. Hosts the WebView2 control, top bar, and status bar.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        ViewModel = new MainViewModel(new AppRuntimeContext(App.Logger, App.Configuration), TryDispatchToUi);
        this.InitializeComponent();
        _liveShellSettingsApplier = new LiveShellSettingsApplier(
            SetAlwaysOnTop,
            ReapplyGlobalHotkey,
            allowMultipleInstances =>
            {
                if (Application.Current is App app)
                {
                    app.ApplySingleInstancePreference(allowMultipleInstances);
                }
            });
        ConfigureWindowChrome();
        RestoreWindowBounds();
        SubscribeToViewModelEvents();
        _runIndicatorTimer = CreateRunIndicatorTimer();
        _webViewRecreationTimer = CreateWebViewRecreationTimer();
        InitializeTrayIcon();
        InitializeGlobalHotkey();
        InitializeAlwaysOnTop();
        AttachWindowEventHandlers();
        AttachRootEventHandlers();
        UpdateThemeSelector(App.Configuration.Settings.AppTheme);
        RestoreCompactModeIfSaved();
    }

    private bool TryDispatchToUi(Action action)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            action();
            return true;
        }

        return DispatcherQueue.TryEnqueue(() => action());
    }
}
