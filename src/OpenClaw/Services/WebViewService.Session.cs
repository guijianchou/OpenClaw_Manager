// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Runtime.InteropServices;

namespace OpenClaw.Services;

public partial class WebViewService
{
    /// <summary>
    /// Clears all browsing data (cookies, cache, local storage) from the WebView2 profile.
    /// </summary>
    public async Task ClearBrowsingDataAsync()
    {
        var coreWebView = GetCoreWebView();
        if (coreWebView is null)
        {
            return;
        }

        try
        {
            _logger.Info("Clearing browsing data.");
            await coreWebView.Profile.ClearBrowsingDataAsync();
            _logger.Info("Browsing data cleared.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to clear browsing data: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens the WebView2 DevTools window.
    /// </summary>
    public void OpenDevTools()
    {
        var coreWebView = GetCoreWebView();
        if (coreWebView is null)
        {
            return;
        }

        try
        {
            coreWebView.OpenDevToolsWindow();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            _logger.Warning($"OpenDevTools skipped because CoreWebView2 became unavailable: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears the session for a specific environment profile.
    /// </summary>
    public async Task ClearEnvironmentSessionAsync(string environmentName)
    {
        if (string.IsNullOrWhiteSpace(environmentName))
        {
            return;
        }

        var coreWebView = GetCoreWebView();
        if (coreWebView is not null &&
            _isInitialized &&
            string.Equals(CurrentEnvironmentName, environmentName, StringComparison.Ordinal))
        {
            _logger.Info($"Clearing active browsing data for environment '{environmentName}'.");
            await coreWebView.Profile.ClearBrowsingDataAsync();
            return;
        }

        await Task.Run(() => DeleteUserDataFolderForEnvironment(environmentName, _logger));
    }

    /// <summary>
    /// Gets the current source URL of the WebView2.
    /// </summary>
    public string? GetCurrentUrl()
    {
        return GetCoreWebView()?.Source;
    }

    /// <summary>
    /// Gets whether the active WebView2 instance already uses the requested environment profile.
    /// </summary>
    public bool IsUsingEnvironmentProfile(string? environmentName)
    {
        return _isInitialized &&
            !string.IsNullOrWhiteSpace(environmentName) &&
            string.Equals(CurrentEnvironmentName, environmentName, StringComparison.Ordinal);
    }
}
