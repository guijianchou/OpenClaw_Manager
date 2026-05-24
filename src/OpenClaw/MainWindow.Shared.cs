// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Dispatching;
using OpenClaw.Services;
using OpenClaw.ViewModels;
using OpenClaw.Views;

namespace OpenClaw;

public sealed partial class MainWindow
{
    private bool _hasPerformedInitialTitleBarRefresh;
    private bool _isDarkThemeActive;
    private bool _hasInitializedWebViewHost;
    private bool _isWindowActive = true;
    private readonly DispatcherQueueTimer _runIndicatorTimer;
    private readonly DispatcherQueueTimer _webViewRecreationTimer;
    private bool _isWindowHidden;
    private SettingsDialog? _settingsWindow;
    private TrayIconService? _trayIconService;
    private GlobalHotkeyService? _globalHotkeyService;
    private readonly TrayClosePolicy _trayClosePolicy = new();
    private readonly WebViewRecreationService _webViewRecreationService = new();
    private readonly LiveShellSettingsApplier _liveShellSettingsApplier;
    private string _lastInstrumentationEvent = string.Empty;

    public MainViewModel ViewModel { get; } = new(App.Logger);
}
