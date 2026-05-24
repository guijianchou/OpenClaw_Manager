// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.ComponentModel;
using OpenClaw.Services;

namespace OpenClaw.ViewModels;

/// <summary>
/// ViewModel for the main application window.
/// Manages environment selection, WebView2 commands, and connection state.
/// </summary>
public partial class MainViewModel : INotifyPropertyChanged, IDisposable
{
    public MainViewModel()
        : this(App.Logger)
    {
    }

    public MainViewModel(IAppLogger logger)
    {
        _webViewService = new WebViewService(logger);
        InitializeCommands();
        SubscribeToServiceEvents();
        InitializeCoordinator();
        LoadEnvironments();
        UpdateStatusPresentation();
    }
}
