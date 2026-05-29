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
        _runtime.Logger.Error($"Async command failed: {ex}");
    }

    private void OnRetry()
    {
        if (_webViewService.RetryNavigation())
        {
            IsErrorVisible = false;
            return;
        }

        _runtime.Logger.Warning("Manual retry was requested, but no retryable WebView navigation is available.");
        ErrorMessage = StringResources.RetryUnavailable;
        IsErrorVisible = true;
        ShowRetryButton = true;
    }

    private void OnReload()
    {
        if (_webViewService.Reload())
        {
            IsErrorVisible = false;
            ShowRetryButton = false;
        }
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
        ApplyConnectionState(ConnectionState.Error);
        ErrorMessage = StringResources.CircuitBreakerRecreationSuppressed;
        IsErrorVisible = true;
        ShowRetryButton = true;
        UpdateStatusPresentation();
    }

    public void ShowWebViewRecreationError(string message)
    {
        ApplyConnectionState(ConnectionState.Error);
        ErrorMessage = message;
        IsErrorVisible = true;
        ShowRetryButton = true;
        UpdateStatusPresentation();
    }

    public void UpdateShellInstrumentation(
        string lastInstrumentationEvent,
        int? totalWebViewRecreations = null,
        int? mergedWebViewRecreationRequests = null)
    {
        _coordinator?.UpdateInstrumentation(
            totalWebViewRecreations: totalWebViewRecreations,
            mergedWebViewRecreationRequests: mergedWebViewRecreationRequests,
            totalControlUiInspectionRequests: _webViewService.TotalControlUiInspectionRequests,
            cachedControlUiInspectionRequests: _webViewService.CachedControlUiInspectionRequests,
            coalescedControlUiInspectionRequests: _webViewService.CoalescedControlUiInspectionRequests,
            deferredSaveRequests: _runtime.Configuration.DeferredSaveRequests,
            deferredSaveCoalescedRequests: _runtime.Configuration.DeferredSaveCoalescedRequests,
            heartbeatRecoveryRequests: _webViewService.HeartbeatRecoveryRequests,
            lastInstrumentationEvent: lastInstrumentationEvent);
    }

    private async Task OnRunDiagnosticsAsync()
    {
        _runtime.Logger.Info("Running diagnostics...");

        var gatewayUrl = _selectedEnvironment?.GatewayUrl;
        var report = await DiagnosticService.RunAllAsync(gatewayUrl, _webViewService, _runtime.Logger);
        UpdateShellInstrumentation(
            lastInstrumentationEvent: "diagnostics.run");

        DiagnosticSummary = report.ToSummary();
        IsDiagnosticVisible = true;

        _runtime.Logger.Info($"Diagnostics complete. Failures: {report.HasFailures}");
    }

    private async Task OnExportDiagnosticBundleAsync()
    {
        _runtime.Logger.Info("Exporting diagnostic bundle...");

        var settingsJson = System.IO.File.Exists(_runtime.Configuration.SettingsFilePath)
            ? await System.IO.File.ReadAllTextAsync(_runtime.Configuration.SettingsFilePath)
            : "{}";

        var diagnosticSummary = DiagnosticSummary;
        var logsDirectory = _runtime.Configuration.LogsDirectory;
        var outputDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var runtimeInfo = DiagnosticBundleService.CollectRuntimeInfo(
            DiagnosticService.GetWebView2RuntimeVersion(_runtime.Logger));

        var outputPath = await Task.Run(() => DiagnosticBundleService.ExportBundleAsync(
            settingsJson,
            logsDirectory,
            diagnosticSummary,
            outputDirectory,
            runtimeInfo));

        _runtime.Logger.Info($"Diagnostic bundle exported to: {outputPath}");
        DiagnosticSummary = $"Diagnostic bundle exported to Desktop:\n{System.IO.Path.GetFileName(outputPath)}";
        IsDiagnosticVisible = true;
    }
}
