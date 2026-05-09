// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.ComponentModel;
using Microsoft.UI.Xaml.Media;

namespace OpenClaw.ViewModels;

/// <summary>
/// ViewModel for the main application window.
/// Manages environment selection, WebView2 commands, and connection state.
/// </summary>
public partial class MainViewModel : INotifyPropertyChanged, IDisposable
{
    public MainViewModel()
    {
        var debounceMs = App.Configuration.Settings.NotifyDebounceSeconds * 1000;
        _taskCompleteNotifier = new Services.TaskCompleteNotifier(debounceMs);
        _taskCompleteNotifier.TaskCompleted += OnTaskCompleted;

        InitializeCommands();
        SubscribeToServiceEvents();
        InitializeCoordinator();
        LoadEnvironments();
        UpdateStatusPresentation();
    }
}
