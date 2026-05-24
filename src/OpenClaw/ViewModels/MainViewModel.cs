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

    public MainViewModel(IAppLogger logger, Action<Action>? dispatchToUi = null)
    {
        _webViewService = new WebViewService(logger);
        _dispatchToUi = dispatchToUi ?? DispatchThroughMainWindow;
        InitializeCommands();
        SubscribeToServiceEvents();
        InitializeCoordinator();
        LoadEnvironments();
        UpdateStatusPresentation();
    }

    private static void DispatchThroughMainWindow(Action action)
    {
        var dispatcher = App.MainWindow?.DispatcherQueue;
        if (dispatcher is null || !dispatcher.TryEnqueue(() => action()))
        {
            action();
        }
    }
}
