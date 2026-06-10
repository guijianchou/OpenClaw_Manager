// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenClaw.Models;
using OpenClaw.Services;
using OpenClaw.Views;

namespace OpenClaw;

public sealed partial class MainWindow
{
    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        ShowSettingsWindow();
    }

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        ToggleAlwaysOnTop();
    }

    private void OnOpenSettingsRequested()
    {
        ShowSettingsWindow();
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow != null)
        {
            ActivateSettingsWindow();
            return;
        }

        _settingsWindow = CreateSettingsWindow();
        ActivateSettingsWindow();
    }

    private void PrewarmSettingsWindow()
    {
        if (_settingsWindow is not null)
        {
            return;
        }

        _settingsWindow = CreateSettingsWindow();
        _settingsWindow.SyncWithCurrentSettings();
    }

    private void QueueSettingsWindowPrewarm()
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => PrewarmSettingsWindow());
    }

    private void OnSettingsSaved(SettingsSaveResult saveResult)
    {
        HandleSettingsSaved(saveResult);
    }

    private SettingsDialog CreateSettingsWindow()
    {
        var settingsWindow = new SettingsDialog(new SettingsPersistenceAdapter(App.Configuration, App.Logger))
        {
            MainViewModel = this.ViewModel,
        };

        settingsWindow.SettingsSaved += OnSettingsSaved;
        settingsWindow.Closed += OnSettingsWindowClosed;
        return settingsWindow;
    }

    private void ActivateSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            return;
        }

        if (!_isSettingsWindowVisible)
        {
            _settingsWindow.ReloadFromCurrentSettings();
        }

        _settingsWindow.SyncWithCurrentSettings();
        _settingsWindow.Activate();
        _isSettingsWindowVisible = true;
    }

    private void OnSettingsWindowClosed(object sender, WindowEventArgs args)
    {
        if (_settingsWindow is null)
        {
            return;
        }

        _settingsWindow.SettingsSaved -= OnSettingsSaved;
        _settingsWindow.Closed -= OnSettingsWindowClosed;
        _settingsWindow = null;
        _isSettingsWindowVisible = false;
        QueueSettingsWindowPrewarm();
    }

    private void CloseSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            return;
        }

        var settingsWindow = _settingsWindow;
        _settingsWindow = null;
        _isSettingsWindowVisible = false;
        settingsWindow.SettingsSaved -= OnSettingsSaved;
        settingsWindow.Closed -= OnSettingsWindowClosed;
        settingsWindow.Close();
    }

    private void HandleSettingsSaved(SettingsSaveResult saveResult)
    {
        if (saveResult.DidChangeEnvironmentState)
        {
            ViewModel.RefreshEnvironments();
        }

        if (saveResult.DidChangeSessionTopology)
        {
            ScheduleWebViewRecreation("settings_saved_topology_changed");
        }

        if (saveResult.DidChangeLiveShellOptions)
        {
            _liveShellSettingsApplier.Apply(saveResult.LiveShellSettingsChange);
        }
    }

    private void OnError(string message)
    {
        App.Logger.Error($"UI error displayed: {message}");
    }

    private void OnWebViewRecreationRequested(string reason)
    {
        ScheduleWebViewRecreation(reason);
    }

    private void OnNavigationTimeoutRecoveryNoLongerNeeded()
    {
        if (!_webViewRecreationService.TryCancelNavigationTimeoutRecovery(out var cancelled))
        {
            return;
        }

        if (cancelled.CancelledPending && _webViewRecreationTimer.IsRunning)
        {
            _webViewRecreationTimer.Stop();
        }

        RecordInstrumentationEvent("webview.recreation.cancelled_after_navigation_recovered", new
        {
            pendingReason = cancelled.PendingReason,
            deferredReason = cancelled.DeferredReason,
            activeReason = cancelled.ActiveReason
        });
    }

    private void OnInfoBarClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        ViewModel.DismissError();
    }

    private void OnDiagnosticInfoBarClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        ViewModel.DismissDiagnostics();
    }

    private async void OnViewLogsRequested()
    {
        try
        {
            await ShowLogViewerAsync();
        }
        catch (Exception ex)
        {
            if (!_isClosing)
            {
                App.Logger.Warning($"Log viewer dialog failed: {ex.Message}");
            }
        }
    }

    private async Task ShowLogViewerAsync()
    {
        if (_isClosing || _isLogViewerOpen || _isAboutDialogOpen)
        {
            return;
        }

        _isLogViewerOpen = true;
        try
        {
            var dialog = new LogViewerDialog
            {
                XamlRoot = this.Content.XamlRoot,
            };
            await dialog.ShowAsync();
        }
        finally
        {
            _isLogViewerOpen = false;
        }
    }

    private async void OnAboutClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await ShowAboutDialogAsync();
        }
        catch (Exception ex)
        {
            if (!_isClosing)
            {
                App.Logger.Warning($"About dialog failed: {ex.Message}");
            }
        }
    }

    private async Task ShowAboutDialogAsync()
    {
        if (_isClosing || _isAboutDialogOpen || _isLogViewerOpen)
        {
            return;
        }

        _isAboutDialogOpen = true;
        try
        {
            var dialog = new AboutDialog
            {
                XamlRoot = this.Content.XamlRoot,
            };
            await dialog.ShowAsync();
        }
        finally
        {
            _isAboutDialogOpen = false;
        }
    }
}
