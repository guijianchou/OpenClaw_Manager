// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.ComponentModel;
using OpenClaw.Abstractions;
using OpenClaw.Services;

namespace OpenClaw.ViewModels;

/// <summary>
/// ViewModel for the main application window.
/// Manages environment selection, WebView2 commands, and connection state.
/// </summary>
public partial class MainViewModel : INotifyPropertyChanged, IDisposable
{
    public MainViewModel(AppRuntimeContext runtime, Func<Action, bool> dispatchToUi)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _dispatchToUi = dispatchToUi ?? throw new ArgumentNullException(nameof(dispatchToUi));
        _latencyService = new ControlUiLatencyService(runtime.Logger);
        _webViewService = new WebViewService(runtime.Logger, _messageOwnership, _dispatchToUi);
        _hostedUiBridge = new HostedUiBridge(runtime.Logger, _messageOwnership);
        InitializeCommands();
        SubscribeToServiceEvents();
        InitializeCoordinator();
        LoadEnvironments();
    }

    private void DispatchUiUpdate(Action action)
    {
        if (!_dispatchToUi(() => RunUiUpdate(action)))
        {
            _runtime.Logger.Warning("UI dispatcher is unavailable; dropping view-model update.");
        }
    }

    private void RunUiUpdate(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                _runtime.Logger.Warning($"View-model UI update failed: {ex.Message}");
            }
        }
    }
}
