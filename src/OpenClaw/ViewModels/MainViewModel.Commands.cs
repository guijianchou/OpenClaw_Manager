// Copyright (c) Lanstack @openclaw. All rights reserved.

using OpenClaw.Helpers;
using OpenClaw.Services;

namespace OpenClaw.ViewModels;

public partial class MainViewModel
{
    private void InitializeCommands()
    {
        OpenSettingsCommand = new SimpleCommand(() => OpenSettingsRequested?.Invoke());
        ReloadCommand = new SimpleCommand(OnReload);
        StopCommand = new AsyncCommand(OnStopAsync, OnAsyncCommandFailed);
        RetryCommand = new SimpleCommand(OnRetry);
        DevToolsCommand = new SimpleCommand(OnDevTools);
        RunDiagnosticsCommand = new AsyncCommand(OnRunDiagnosticsAsync, OnAsyncCommandFailed);
        ExportDiagnosticBundleCommand = new AsyncCommand(OnExportDiagnosticBundleAsync, OnAsyncCommandFailed);
        ViewLogsCommand = new SimpleCommand(() => ViewLogsRequested?.Invoke());
    }

    private void OnAsyncCommandFailed(Exception ex)
    {
        App.Logger.Error($"Async command failed: {ex}");
    }

    private void OnRetry()
    {
        IsErrorVisible = false;
        _webViewService.RetryNavigation();
    }

    private void OnReload()
    {
        _webViewService.Reload();
    }

    private async Task OnStopAsync()
    {
        await _webViewService.StopAsync();
    }

    private void OnDevTools()
    {
        _webViewService.OpenDevTools();
    }

    /// <summary>
    /// Dismisses the error InfoBar.
    /// </summary>
    public void DismissError()
    {
        IsErrorVisible = false;
    }

    public void DismissDiagnostics()
    {
        IsDiagnosticVisible = false;
    }

    /// <summary>
    /// Shows an error indicating the WebView recreation circuit breaker has tripped.
    /// </summary>
    public void ShowCircuitBreakerError()
    {
        ErrorMessage = "WebView2 recreation failed repeatedly. Click Reload to retry.";
        IsErrorVisible = true;
        ShowRetryButton = true;
    }

    private async Task OnRunDiagnosticsAsync()
    {
        App.Logger.Info("Running diagnostics...");

        var gatewayUrl = _selectedEnvironment?.GatewayUrl;
        var report = await DiagnosticService.RunAllAsync(gatewayUrl, _webViewService);
        _coordinator?.UpdateInstrumentation(
            totalControlUiInspectionRequests: _webViewService.TotalControlUiInspectionRequests,
            cachedControlUiInspectionRequests: _webViewService.CachedControlUiInspectionRequests,
            coalescedControlUiInspectionRequests: _webViewService.CoalescedControlUiInspectionRequests,
            deferredSaveRequests: App.Configuration.DeferredSaveRequests,
            deferredSaveCoalescedRequests: App.Configuration.DeferredSaveCoalescedRequests,
            heartbeatRecoveryRequests: _webViewService.HeartbeatRecoveryRequests,
            lastInstrumentationEvent: "diagnostics.run");

        DiagnosticSummary = report.ToSummary();
        IsDiagnosticVisible = true;

        App.Logger.Info($"Diagnostics complete. Failures: {report.HasFailures}");
    }

    private async Task OnExportDiagnosticBundleAsync()
    {
        App.Logger.Info("Exporting diagnostic bundle...");

        var settingsJson = System.IO.File.Exists(App.Configuration.SettingsFilePath)
            ? await System.IO.File.ReadAllTextAsync(App.Configuration.SettingsFilePath)
            : "{}";

        var diagnosticSummary = DiagnosticSummary;
        var logsDirectory = App.Configuration.LogsDirectory;
        var outputDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        var outputPath = await DiagnosticBundleService.ExportBundleAsync(
            settingsJson,
            logsDirectory,
            diagnosticSummary,
            outputDirectory);

        App.Logger.Info($"Diagnostic bundle exported to: {outputPath}");
        DiagnosticSummary = $"Diagnostic bundle exported to Desktop:\n{System.IO.Path.GetFileName(outputPath)}";
        IsDiagnosticVisible = true;
    }
}
