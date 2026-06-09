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
        RunDiagnosticsCommand = new AsyncCommand(RunDiagnosticsAsync, OnAsyncCommandFailed);
        ExportDiagnosticBundleCommand = new AsyncCommand(ExportDiagnosticBundleAsync, OnAsyncCommandFailed);
        ViewLogsCommand = new SimpleCommand(() => ViewLogsRequested?.Invoke());
    }

    private void OnAsyncCommandFailed(Exception ex)
    {
        _runtime.Logger.Error($"Async command failed: {ex}");
        ErrorMessage = string.Format(StringResources.AsyncCommandFailedFormat, ex.Message);
        IsErrorVisible = true;
        ShowRetryButton = false;
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
        var result = OpenDevTools();
        if (result.Succeeded)
        {
            return;
        }

        ErrorMessage = FormatDevToolsOpenResult(result);
        IsErrorVisible = true;
        ShowRetryButton = false;
    }

    public WebViewService.DevToolsOpenResult OpenDevTools() =>
        _webViewService.OpenDevTools();

    public static string FormatDevToolsOpenResult(WebViewService.DevToolsOpenResult result)
    {
        return result.Status switch
        {
            WebViewService.DevToolsOpenStatus.Opened => StringResources.SettingsDevToolsOpened,
            WebViewService.DevToolsOpenStatus.Unavailable => StringResources.SettingsDevToolsUnavailable,
            WebViewService.DevToolsOpenStatus.Disabled => StringResources.SettingsDevToolsDisabled,
            WebViewService.DevToolsOpenStatus.Failed => string.Format(
                StringResources.SettingsDevToolsOpenFailedFormat,
                result.Message ?? StringResources.SettingsValidationSaveFailedUnknown),
            _ => StringResources.SettingsDevToolsUnavailable,
        };
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

    public void ShowGlobalHotkeyRegistrationError(string message)
    {
        ErrorMessage = message;
        IsErrorVisible = true;
        ShowRetryButton = false;
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

    public async Task RunDiagnosticsAsync()
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

    public async Task ExportDiagnosticBundleAsync()
    {
        _runtime.Logger.Info("Exporting diagnostic bundle...");

        var settingsJson = System.IO.File.Exists(_runtime.Configuration.SettingsFilePath)
            ? await System.IO.File.ReadAllTextAsync(_runtime.Configuration.SettingsFilePath)
            : "{}";

        var diagnosticSummary = DiagnosticSummary;
        var logsDirectory = _runtime.Configuration.LogsDirectory;
        var outputDirectory = ResolveDiagnosticBundleOutputDirectory(logsDirectory);
        var runtimeInfo = DiagnosticBundleService.CollectRuntimeInfo(
            DiagnosticService.GetWebView2RuntimeVersion(_runtime.Logger));

        var outputPath = await Task.Run(() => DiagnosticBundleService.ExportBundleAsync(
            settingsJson,
            logsDirectory,
            diagnosticSummary,
            outputDirectory,
            runtimeInfo));

        _runtime.Logger.Info($"Diagnostic bundle exported to: {outputPath}");
        DiagnosticSummary = string.Format(StringResources.DiagnosticBundleExportedFormat, outputPath);
        IsDiagnosticVisible = true;
    }

    private static string ResolveDiagnosticBundleOutputDirectory(string logsDirectory)
    {
        foreach (var candidate in EnumerateDiagnosticBundleOutputCandidates(logsDirectory))
        {
            if (TryEnsureWritableDirectory(candidate))
            {
                return candidate;
            }
        }

        var fallbackRoot = string.IsNullOrWhiteSpace(logsDirectory)
            ? AppContext.BaseDirectory
            : logsDirectory;
        var fallbackDirectory = System.IO.Path.Combine(
            fallbackRoot,
            StringResources.DiagnosticBundleExportFallbackDirectoryName);
        _ = TryEnsureWritableDirectory(fallbackDirectory);
        return fallbackDirectory;
    }

    private static IEnumerable<string> EnumerateDiagnosticBundleOutputCandidates(string logsDirectory)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var localFallback = string.IsNullOrWhiteSpace(localAppData)
            ? null
            : System.IO.Path.Combine(localAppData, "OpenClaw", StringResources.DiagnosticBundleExportFallbackDirectoryName);

        foreach (var candidate in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            localFallback,
            string.IsNullOrWhiteSpace(logsDirectory)
                ? null
                : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(logsDirectory) ?? logsDirectory, StringResources.DiagnosticBundleExportFallbackDirectoryName),
        })
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static bool TryEnsureWritableDirectory(string directory)
    {
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            var probePath = System.IO.Path.Combine(directory, $".openclaw-write-test-{Guid.NewGuid():N}.tmp");
            System.IO.File.WriteAllText(probePath, string.Empty);
            System.IO.File.Delete(probePath);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
