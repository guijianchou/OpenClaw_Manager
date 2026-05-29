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
    private bool _isSettingsWindowVisible;
    private bool _isLogViewerOpen;
    private bool _isAboutDialogOpen;
    private TrayIconService? _trayIconService;
    private GlobalHotkeyService? _globalHotkeyService;
    private readonly TrayClosePolicy _trayClosePolicy = new();
    private readonly WebViewRecreationService _webViewRecreationService = new();
    private readonly LiveShellSettingsApplier _liveShellSettingsApplier;
    private readonly CancellationTokenSource _windowLifetimeCts = new();
    private Task? _webViewRecreationTask;
    private string _lastInstrumentationEvent = string.Empty;
    private bool _isClosing;

    public MainViewModel ViewModel { get; }
}
