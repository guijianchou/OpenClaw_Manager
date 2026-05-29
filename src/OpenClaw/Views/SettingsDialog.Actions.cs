// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenClaw.Helpers;
using OpenClaw.Models;

namespace OpenClaw.Views;

public sealed partial class SettingsDialog
{
    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TrySaveSettings(out var saveResult))
        {
            return;
        }

        if (saveResult.DidChangeLanguage)
        {
            App.ApplyLanguage(ViewModel.SelectedLanguage);
        }

        SettingsSaved?.Invoke(saveResult);
        this.Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void OnRunDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        MainViewModel?.RunDiagnosticsCommand.Execute(null);
        this.Close();
    }

    private void OnViewLogsClick(object sender, RoutedEventArgs e)
    {
        var mainViewModel = MainViewModel;
        this.Close();
        mainViewModel?.ViewLogsCommand.Execute(null);
    }

    private void OnDevToolsClick(object sender, RoutedEventArgs e)
    {
        MainViewModel?.DevToolsCommand.Execute(null);
        this.Close();
    }

    private void OnExportDiagnosticBundleClick(object sender, RoutedEventArgs e)
    {
        MainViewModel?.ExportDiagnosticBundleCommand.Execute(null);
        this.Close();
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

    private async Task ClearEnvironmentSessionAsync(Button? button)
    {
        if (MainViewModel is null ||
            button?.Tag is not string environmentName ||
            string.IsNullOrWhiteSpace(environmentName))
        {
            ShowSessionMessage(InfoBarSeverity.Warning, StringResources.SettingsSessionResetSelectEnvironment);
            return;
        }

        button.IsEnabled = false;
        try
        {
            await MainViewModel.ClearSessionForEnvironmentAsync(environmentName);
            ShowSessionMessage(
                InfoBarSeverity.Informational,
                string.Format(StringResources.SettingsSessionResetCompleted, environmentName));
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private static string GetSessionButtonEnvironmentName(Button? button)
    {
        return button?.Tag is string environmentName && !string.IsNullOrWhiteSpace(environmentName)
            ? environmentName
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
