// Copyright (c) Lanstack @openclaw. All rights reserved.

using OpenClaw.Helpers;
using OpenClaw.Models;
using OpenClaw.Services;
using OpenClaw.ViewModels;

namespace OpenClaw.Views;

public sealed partial class SettingsDialog
{
    private const string LanguagePanelTag = "Language";
    private const string ShellPanelTag = "Shell";
    private const string EnvironmentsPanelTag = "Environments";
    private const string SessionsPanelTag = "Sessions";
    private const string DevToolsPanelTag = "DevTools";
    private bool _hasPerformedInitialTitleBarRefresh;
    private bool _hasRestoredSettingsBoundsAfterActivation;
    private bool _isDarkThemeActive;
    private bool _isSyncingLanguageSelection;
    private readonly SettingsPersistenceAdapter _settingsPersistence;

    public SettingsViewModel ViewModel { get; private set; }

    /// <summary>
    /// Gets the main view model for developer tools commands.
    /// </summary>
    public MainViewModel? MainViewModel { get; set; }

    /// <summary>
    /// Raised when settings are saved, so MainWindow can refresh.
    /// </summary>
    public event Action<SettingsSaveResult>? SettingsSaved;

    private static string ValidationErrorTitle => StringResources.SettingsValidationError;
}
