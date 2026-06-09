// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace OpenClaw.Services;

public partial class WebViewService
{
    /// <summary>
    /// Initializes the WebView2 control with a custom user data folder.
    /// </summary>
    public async Task InitializeAsync(
        WebView2 webView,
        string environmentName,
        string gatewayUrl,
        CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
        {
            return;
        }

        DetachCurrentWebView();
        _webView = webView;
        _coreWebView = null;
        var initializationGeneration = _generations.Next();
        _messageOwnership.ResetForNewWebView();
        CurrentEnvironmentName = environmentName;
        CurrentEnvironmentGatewayUrl = gatewayUrl;
        _isInitialized = false;

        try
        {
            await MigrateLegacyUserDataFolderIfNeededAsync(environmentName, gatewayUrl, _logger, cancellationToken);
            var userDataFolder = GetUserDataFolderForEnvironment(environmentName, gatewayUrl);
            Directory.CreateDirectory(userDataFolder);
            await WriteProfileIdentityMarkerAsync(userDataFolder, gatewayUrl, _logger, cancellationToken);

            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, null);
            cancellationToken.ThrowIfCancellationRequested();

            await webView.EnsureCoreWebView2Async(environment);
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsCurrentInitialization(webView, initializationGeneration))
            {
                return;
            }

            var coreWebView = TryGetCoreWebView2(webView);
            if (coreWebView is null)
            {
                throw new InvalidOperationException("CoreWebView2 became unavailable immediately after initialization.");
            }

            if (!IsCurrentInitialization(webView, initializationGeneration))
            {
                return;
            }

            _coreWebView = coreWebView;

            // Make WebView2 follow system Light/Dark theme preferred scheme
            coreWebView.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Auto;

            // Set default background to transparent (blends with Mica)
            webView.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent;

            var hostGeneration = _hostGeneration;
            _navigationStartingHandler = CreateNavigationStartingHandler(hostGeneration);
            _navigationCompletedHandler = CreateNavigationCompletedHandler(hostGeneration);
            _processFailedHandler = CreateProcessFailedHandler(hostGeneration);
            _webMessageReceivedHandler = CreateWebMessageReceivedHandler(hostGeneration);

            coreWebView.NavigationStarting += _navigationStartingHandler;
            coreWebView.NavigationCompleted += _navigationCompletedHandler;
            coreWebView.ProcessFailed += _processFailedHandler;
            coreWebView.WebMessageReceived += _webMessageReceivedHandler;

            // Allow file input dialog
            coreWebView.Settings.AreDefaultContextMenusEnabled = true;
            coreWebView.Settings.IsStatusBarEnabled = false;
            coreWebView.Settings.AreDevToolsEnabled = ShouldEnableDevTools();

            coreWebView.Settings.IsGeneralAutofillEnabled = true;

            _isInitialized = true;
            _logger.Info("WebView2 initialized successfully.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.Info("WebView2 initialization cancelled.");
        }
        catch (Exception ex)
        {
            _logger.Error($"WebView2 initialization failed: {ex}");
            SetState(ConnectionState.Error);
            NavigationErrorOccurred?.Invoke($"WebView2 initialization failed: {ex.Message}");
        }
    }

    public void DetachCurrentWebViewHost()
    {
        if (_isDisposed)
        {
            return;
        }

        DetachCurrentWebView();
    }

    private void DetachCurrentWebView()
    {
        CancelStatusProbeLoop();
        StopHeartbeat();
        CancelNavigationStartWatchdog();
        CancelNavigationCompletionWatchdog();
        _hostGeneration++;
        _generations.Next();
        _messageOwnership.BeginNavigation();
        InvalidateControlUiInspectionCache();
        CancelNavigationCancellation();

        var coreWebView = GetCoreWebView();
        if (coreWebView is not null)
        {
            if (_navigationStartingHandler is not null)
            {
                coreWebView.NavigationStarting -= _navigationStartingHandler;
            }

            if (_navigationCompletedHandler is not null)
            {
                coreWebView.NavigationCompleted -= _navigationCompletedHandler;
            }

            if (_processFailedHandler is not null)
            {
                coreWebView.ProcessFailed -= _processFailedHandler;
            }

            if (_webMessageReceivedHandler is not null)
            {
                coreWebView.WebMessageReceived -= _webMessageReceivedHandler;
            }
        }

        _navigationStartingHandler = null;
        _navigationCompletedHandler = null;
        _processFailedHandler = null;
        _webMessageReceivedHandler = null;
        _webView = null;
        _coreWebView = null;
        CurrentEnvironmentGatewayUrl = null;
        _isInitialized = false;
        _statusInspector.SetUnknownSnapshot();
        _lastPublishedControlUiSnapshot = ControlUiProbeSnapshot.Unknown;
        _lastLifecycleLogKey = null;
    }

    public void Dispose()
    {
        _isDisposed = true;
        DetachCurrentWebView();
        CancelNavigationStartWatchdog();
        CancelNavigationCompletionWatchdog();
        CancelNavigationCancellation();
        _statusInspector.Dispose();
        _heartbeatRuntime.Dispose();
    }

    private static CoreWebView2? TryGetCoreWebView2(WebView2? webView)
    {
        if (webView is null)
        {
            return null;
        }

        try
        {
            return webView.CoreWebView2;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return null;
        }
    }

    private bool ShouldEnableDevTools()
    {
#if DEBUG
        return true;
#else
        return _shouldEnableDevTools();
#endif
    }

    private CoreWebView2? GetCoreWebView()
    {
        if (_coreWebView is not null)
        {
            return _coreWebView;
        }

        _coreWebView = TryGetCoreWebView2(_webView);
        return _coreWebView;
    }

    private bool IsCurrentInitialization(WebView2 webView, int generation)
    {
        return !_isDisposed &&
            _generations.IsCurrent(generation) &&
            ReferenceEquals(webView, _webView);
    }

    private bool IsCurrentHost(int hostGeneration)
    {
        return !_isDisposed &&
            _coreWebView is not null &&
            _hostGeneration == hostGeneration;
    }

    private bool IsCurrentNavigation(ulong navigationId, int generation, int hostGeneration)
    {
        return !_isDisposed &&
            navigationId == _currentNavigationId &&
            _generations.IsCurrent(generation) &&
            IsCurrentHost(hostGeneration);
    }
}
