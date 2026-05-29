// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using OpenClaw.Services;
using OpenClaw.ViewModels;
using Windows.Graphics;

namespace OpenClaw.Views;

/// <summary>
/// Settings window with Windows Settings-style sidebar navigation.
/// Resizable, Mica-backed, independent window.
/// </summary>
public sealed partial class SettingsDialog : Window
{
    public SettingsDialog()
        : this(new SettingsPersistenceAdapter(App.Configuration, App.Logger))
    {
    }

    internal SettingsDialog(SettingsPersistenceAdapter settingsPersistence)
    {
        _settingsPersistence = settingsPersistence ?? throw new ArgumentNullException(nameof(settingsPersistence));
        ViewModel = new SettingsViewModel(_settingsPersistence);
        this.InitializeComponent();
        ConfigureWindowChrome();
        AttachRootEventHandlers();
        InitializeEnvironmentBindings();
        InitializeNavigationState();
    }
}
