// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
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
        ViewModel = new MainViewModel(App.Logger, DispatchToUi);
        this.InitializeComponent();
        _liveShellSettingsApplier = new LiveShellSettingsApplier(SetAlwaysOnTop, ReapplyGlobalHotkey);
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

    private void DispatchToUi(Action action)
    {
        if (!DispatcherQueue.TryEnqueue(() => action()))
        {
            action();
        }
    }
}
