// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenClaw.Helpers;
using OpenClaw.Models;
using OpenClaw.Services;

namespace OpenClaw.Views;

public sealed partial class SettingsDialog
{
    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TrySaveSettings(out var saveResult))
        {
            return;
        }

        SettingsSaved?.Invoke(saveResult);
        this.Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private async void OnRunDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        await RunDevToolsActionAsync(
            sender as Button,
            StringResources.SettingsDiagnosticsRunning,
            async mainViewModel =>
            {
                await mainViewModel.RunDiagnosticsAsync();
                ShowDevToolsMessage(InfoBarSeverity.Informational, mainViewModel.DiagnosticSummary);
            });
    }

    private void OnViewLogsClick(object sender, RoutedEventArgs e)
    {
        var mainViewModel = MainViewModel;
        this.Close();
        mainViewModel?.ViewLogsCommand.Execute(null);
    }

    private void OnDevToolsClick(object sender, RoutedEventArgs e)
    {
        var result = MainViewModel?.OpenDevTools();
        if (result is null)
        {
            ShowDevToolsMessage(InfoBarSeverity.Warning, StringResources.SettingsDevToolsUnavailable);
            return;
        }

        var severity = result.Value.Status switch
        {
            WebViewService.DevToolsOpenStatus.Opened => InfoBarSeverity.Informational,
            WebViewService.DevToolsOpenStatus.Failed => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Warning,
        };
        ShowDevToolsMessage(severity, OpenClaw.ViewModels.MainViewModel.FormatDevToolsOpenResult(result.Value));
    }

    private async void OnExportDiagnosticBundleClick(object sender, RoutedEventArgs e)
    {
        await RunDevToolsActionAsync(
            sender as Button,
            StringResources.SettingsDiagnosticBundleExporting,
            async mainViewModel =>
            {
                await mainViewModel.ExportDiagnosticBundleAsync();
                ShowDevToolsMessage(InfoBarSeverity.Informational, mainViewModel.DiagnosticSummary);
            });
    }

    private void OnResetHotkeyClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetGlobalHotkey();
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (!TryApplyEdit())
        {
            return;
        }

        ValidationInfoBar.IsOpen = false;
    }

    private async void OnClearEnvironmentSessionClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await ClearEnvironmentSessionAsync(sender as Button);
        }
        catch (Exception ex)
        {
            var environmentName = GetSessionButtonEnvironmentName(sender as Button);
            App.Logger.Warning($"Failed to clear environment session: {ex.Message}");
            ShowSessionMessage(
                InfoBarSeverity.Error,
                string.Format(StringResources.SettingsSessionResetFailedFormat, environmentName, ex.Message));
        }
    }

    private void ShowEnvironmentMessage(string title, InfoBarSeverity severity, string? message = null)
    {
        ValidationInfoBar.Title = title;
        ValidationInfoBar.Severity = severity;
        ValidationInfoBar.Message = message ?? ViewModel.ValidationMessage;
        ValidationInfoBar.IsOpen = true;
    }

    private void ShowSessionMessage(InfoBarSeverity severity, string message)
    {
        SessionInfoBar.Title = StringResources.SettingsSessionReset;
        SessionInfoBar.Severity = severity;
        SessionInfoBar.Message = message;
        SessionInfoBar.IsOpen = true;
    }

    private void ShowDevToolsMessage(InfoBarSeverity severity, string message)
    {
        DevToolsInfoBar.Title = StringResources.DiagnosticsTitle;
        DevToolsInfoBar.Severity = severity;
        DevToolsInfoBar.Message = message;
        DevToolsInfoBar.IsOpen = true;
    }

    private async Task RunDevToolsActionAsync(
        Button? button,
        string inProgressMessage,
        Func<OpenClaw.ViewModels.MainViewModel, Task> action)
    {
        var mainViewModel = MainViewModel;
        if (mainViewModel is null)
        {
            return;
        }

        if (button is not null)
        {
            button.IsEnabled = false;
        }

        ShowDevToolsMessage(InfoBarSeverity.Informational, inProgressMessage);
        try
        {
            await action(mainViewModel);
        }
        catch (Exception ex)
        {
            App.Logger.Warning($"Settings developer tool action failed: {ex.Message}");
            ShowDevToolsMessage(
                InfoBarSeverity.Error,
                string.Format(StringResources.AsyncCommandFailedFormat, ex.Message));
        }
        finally
        {
            if (button is not null)
            {
                button.IsEnabled = true;
            }
        }
    }

    private async Task ClearEnvironmentSessionAsync(Button? button)
    {
        if (MainViewModel is null ||
            button?.Tag is not EnvironmentConfig environment ||
            string.IsNullOrWhiteSpace(environment.Name))
        {
            ShowSessionMessage(InfoBarSeverity.Warning, StringResources.SettingsSessionResetSelectEnvironment);
            return;
        }

        button.IsEnabled = false;
        try
        {
            await MainViewModel.ClearSessionForEnvironmentAsync(environment);
            ShowSessionMessage(
                InfoBarSeverity.Informational,
                string.Format(StringResources.SettingsSessionResetCompleted, environment.Name));
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private static string GetSessionButtonEnvironmentName(Button? button)
    {
        return button?.Tag is EnvironmentConfig environment && !string.IsNullOrWhiteSpace(environment.Name)
            ? environment.Name
            : StringResources.SettingsSessionReset;
    }

    private bool TryApplyEdit()
    {
        if (ViewModel.TryApplyEdit())
        {
            return true;
        }

        ShowEnvironmentMessage(ValidationErrorTitle, InfoBarSeverity.Error);
        return false;
    }

    private bool TrySaveSettings(out SettingsSaveResult result)
    {
        result = default;
        if (ViewModel.IsEditing && !TryApplyEdit())
        {
            return false;
        }

        if (ViewModel.SaveAll(out result))
        {
            return true;
        }

        ShowEnvironmentMessage(ValidationErrorTitle, InfoBarSeverity.Error);
        return false;
    }
}
