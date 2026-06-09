// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenClaw.Helpers;
using OpenClaw.Models;
using OpenClaw.Services;

namespace OpenClaw.ViewModels;

/// <summary>
/// ViewModel for the Settings dialog.
/// Manages CRUD operations on gateway environment configurations.
/// </summary>
public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly Dictionary<EnvironmentConfig, string> _originalNames = [];
    private readonly Dictionary<EnvironmentConfig, EnvironmentConfig> _originalSnapshots = [];
    private readonly SettingsPersistenceAdapter _settingsPersistence;
    private readonly string? _originalSelectedEnvironmentName;
    private bool _didEditSelectedLanguage;
    private bool _didEditEnableDevLog;
    private bool _didEditEnableDevTools;
    private bool _didEditMinimizeToTray;
    private bool _didEditCloseToTray;
    private bool _didEditAllowMultipleInstances;
    private bool _didEditEnableGlobalHotkey;
    private bool _didEditGlobalHotkey;
    private bool _didEditAlwaysOnTop;
    private EnvironmentConfig? _selectedEnvironment;
    private string _editName = string.Empty;
    private string _editUrl = string.Empty;
    private bool _editIsDefault;
    private bool _isEditing;
    private string _selectedLanguage = "System";
    private bool _enableDevLog;
    private bool _enableDevTools;
    private bool _minimizeToTray;
    private bool _closeToTray;
    private bool _allowMultipleInstances;
    private bool _enableGlobalHotkey;
    private string _globalHotkey = string.Empty;
    private bool _alwaysOnTop;
    private string _validationMessage = string.Empty;

    internal SettingsViewModel(SettingsPersistenceAdapter settingsPersistence)
    {
        _settingsPersistence = settingsPersistence ?? throw new ArgumentNullException(nameof(settingsPersistence));
        var settings = _settingsPersistence.Current;
        _originalSelectedEnvironmentName = settings.SelectedEnvironmentName;

        // Load a copy of environments so we can cancel without persisting
        foreach (var env in settings.Environments)
        {
            var clone = env.Clone();
            Environments.Add(clone);
            _originalNames[clone] = env.Name;
            _originalSnapshots[clone] = env.Clone();
        }

        // Load language preference
        _selectedLanguage = settings.AppLanguage ?? "System";
        _enableDevLog = settings.Diagnostics.EnableVerboseRecoveryLogging;
        _enableDevTools = settings.Diagnostics.EnableDevTools;
        _minimizeToTray = settings.MinimizeToTray;
        _closeToTray = settings.CloseToTray;
        _allowMultipleInstances = settings.AllowMultipleInstances;
        _enableGlobalHotkey = settings.EnableGlobalHotkey;
        _globalHotkey = settings.GlobalHotkey;
        _alwaysOnTop = settings.AlwaysOnTop;
        _validationMessage = StringResources.SettingsValidationDefaultMessage;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<EnvironmentConfig> Environments { get; } = [];

    public EnvironmentConfig? SelectedEnvironment
    {
        get => _selectedEnvironment;
        set
        {
            _selectedEnvironment = value;
            OnPropertyChanged();
            LoadEditFields();
        }
    }

    public string EditName
    {
        get => _editName;
        set { _editName = value; OnPropertyChanged(); }
    }

    public string EditUrl
    {
        get => _editUrl;
        set { _editUrl = value; OnPropertyChanged(); }
    }

    public bool EditIsDefault
    {
        get => _editIsDefault;
        set { _editIsDefault = value; OnPropertyChanged(); }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set { _isEditing = value; OnPropertyChanged(); }
    }

    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (string.Equals(_selectedLanguage, value, StringComparison.Ordinal))
            {
                return;
            }

            _selectedLanguage = value;
            _didEditSelectedLanguage = true;
            OnPropertyChanged();
        }
    }

    public bool EnableDevLog
    {
        get => _enableDevLog;
        set
        {
            if (_enableDevLog == value)
            {
                return;
            }

            _enableDevLog = value;
            _didEditEnableDevLog = true;
            OnPropertyChanged();
        }
    }

    public bool EnableDevTools
    {
        get => _enableDevTools;
        set
        {
            if (_enableDevTools == value)
            {
                return;
            }

            _enableDevTools = value;
            _didEditEnableDevTools = true;
            OnPropertyChanged();
        }
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set
        {
            if (_minimizeToTray == value)
            {
                return;
            }

            _minimizeToTray = value;
            _didEditMinimizeToTray = true;
            OnPropertyChanged();
        }
    }

    public bool CloseToTray
    {
        get => _closeToTray;
        set
        {
            if (_closeToTray == value)
            {
                return;
            }

            _closeToTray = value;
            _didEditCloseToTray = true;
            OnPropertyChanged();
        }
    }

    public bool AllowMultipleInstances
    {
        get => _allowMultipleInstances;
        set
        {
            if (_allowMultipleInstances == value)
            {
                return;
            }

            _allowMultipleInstances = value;
            _didEditAllowMultipleInstances = true;
            OnPropertyChanged();
        }
    }

    public bool EnableGlobalHotkey
    {
        get => _enableGlobalHotkey;
        set
        {
            if (_enableGlobalHotkey == value)
            {
                return;
            }

            _enableGlobalHotkey = value;
            _didEditEnableGlobalHotkey = true;
            OnPropertyChanged();
        }
    }

    public string GlobalHotkey
    {
        get => _globalHotkey;
        set
        {
            if (string.Equals(_globalHotkey, value, StringComparison.Ordinal))
            {
                return;
            }

            _globalHotkey = value;
            _didEditGlobalHotkey = true;
            OnPropertyChanged();
        }
    }

    public bool AlwaysOnTop
    {
        get => _alwaysOnTop;
        set
        {
            if (_alwaysOnTop == value)
            {
                return;
            }

            _alwaysOnTop = value;
            _didEditAlwaysOnTop = true;
            OnPropertyChanged();
        }
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            _validationMessage = value;
            OnPropertyChanged();
        }
    }

    public bool DidChangeSessionTopology { get; private set; }

    public bool DidChangeEnvironmentState { get; private set; }

    public void ResetGlobalHotkey()
    {
        EnableGlobalHotkey = true;
        GlobalHotkey = new AppSettings().GlobalHotkey;
    }

    /// <summary>
    /// Adds a new environment with placeholder values.
    /// </summary>
    public void AddEnvironment()
    {
        var env = new EnvironmentConfig
        {
            Name = string.Format(StringResources.SettingsGeneratedEnvironmentName, Environments.Count + 1),
            GatewayUrl = "https://",
            IsDefault = Environments.Count == 0,
        };
        Environments.Add(env);
        _originalNames[env] = env.Name;
        _originalSnapshots[env] = env.Clone();
        DidChangeEnvironmentState = true;
        DidChangeSessionTopology = true;
        SelectedEnvironment = env;
        IsEditing = true;
    }

    /// <summary>
    /// Removes the currently selected environment.
    /// </summary>
    public void RemoveEnvironment()
    {
        if (_selectedEnvironment is null)
        {
            return;
        }

        _originalNames.Remove(_selectedEnvironment);
        _originalSnapshots.Remove(_selectedEnvironment);
        Environments.Remove(_selectedEnvironment);
        DidChangeEnvironmentState = true;
        DidChangeSessionTopology = true;
        SelectedEnvironment = Environments.FirstOrDefault();
    }

    /// <summary>
    /// Applies edit field values back to the selected environment.
    /// </summary>
    public bool TryApplyEdit()
    {
        if (_selectedEnvironment is null)
        {
            ValidationMessage = StringResources.SettingsValidationSelectEnvironment;
            return false;
        }

        var draft = CreateDraftEnvironment();

        if (!TryValidateEnvironment(draft, out var errorMessage))
        {
            ValidationMessage = errorMessage;
            return false;
        }

        _selectedEnvironment.ApplyFrom(draft);
        DidChangeEnvironmentState |= DidEnvironmentMetadataChange(_selectedEnvironment);
        DidChangeSessionTopology |= DidEnvironmentSessionIdentityChange(_selectedEnvironment);

        // If setting as default, clear others
        if (EditIsDefault)
        {
            foreach (var env in Environments)
            {
                env.IsDefault = env == _selectedEnvironment;
            }
        }
        else
        {
            _selectedEnvironment.IsDefault = false;
        }

        IsEditing = false;

        ValidationMessage = StringResources.SettingsValidationDefaultMessage;
        return true;
    }

    /// <summary>
    /// Saves all environments to the configuration service.
    /// Returns true if save was successful.
    /// </summary>
    public bool SaveAll(out SettingsSaveResult result)
    {
        result = default;

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var env in Environments)
        {
            if (!TryValidateEnvironment(env, out var errorMessage))
            {
                ValidationMessage = errorMessage;
                return false;
            }

            if (!seenNames.Add(env.Name.Trim()))
            {
                ValidationMessage = string.Format(StringResources.SettingsValidationDuplicateEnvironment, env.Name);
                return false;
            }
        }

        if (!TryValidateHotkey(out var hotkeyErrorMessage))
        {
            ValidationMessage = hotkeyErrorMessage;
            return false;
        }

        var currentSettings = _settingsPersistence.Current;
        var candidateSettings = currentSettings.Clone();
        var didChangeEnvironmentState = DidChangeEnvironmentState || HasEnvironmentMetadataChanges();
        var didChangeSessionTopology = DidChangeSessionTopology || HasEnvironmentSessionIdentityChanges();
        var beforeLanguage = currentSettings.AppLanguage ?? "System";
        var beforeDevTools = currentSettings.Diagnostics.EnableDevTools;
        var beforeLiveSettings = LiveShellSettings.From(currentSettings);

        if (didChangeEnvironmentState)
        {
            candidateSettings.Environments = Environments.Select(env => env.Clone()).ToList();
            EnsureAtLeastOneDefault(candidateSettings.Environments);
            candidateSettings.SelectedEnvironmentName = ResolveSelectedEnvironmentName(candidateSettings);
        }

        ApplyChangedShellSettings(candidateSettings);

        var saveResult = _settingsPersistence.Save(candidateSettings);
        if (!saveResult.Succeeded)
        {
            ValidationMessage = string.Format(
                StringResources.SettingsValidationSaveFailedFormat,
                saveResult.ErrorMessage ?? StringResources.SettingsValidationSaveFailedUnknown);
            return false;
        }

        ValidationMessage = StringResources.SettingsValidationDefaultMessage;
        var afterLiveSettings = LiveShellSettings.From(candidateSettings);
        result = new SettingsSaveResult(
            didChangeEnvironmentState,
            didChangeSessionTopology,
            !string.Equals(beforeLanguage, candidateSettings.AppLanguage ?? "System", StringComparison.Ordinal),
            beforeDevTools != candidateSettings.Diagnostics.EnableDevTools,
            new LiveShellSettingsChange(beforeLiveSettings, afterLiveSettings));
        return true;
    }

    private void LoadEditFields()
    {
        if (_selectedEnvironment is not null)
        {
            EditName = _selectedEnvironment.Name;
            EditUrl = _selectedEnvironment.GatewayUrl;
            EditIsDefault = _selectedEnvironment.IsDefault;
            IsEditing = true;
        }
        else
        {
            EditName = string.Empty;
            EditUrl = string.Empty;
            EditIsDefault = false;
            IsEditing = false;
        }
    }

    private string? ResolveSelectedEnvironmentName(AppSettings settings)
    {
        var currentSelection = FindEnvironmentByOriginalOrCurrentName(settings.SelectedEnvironmentName);
        if (currentSelection is not null)
        {
            return currentSelection.Name;
        }

        var originalSelection = FindEnvironmentByOriginalOrCurrentName(_originalSelectedEnvironmentName);
        if (originalSelection is not null)
        {
            return originalSelection.Name;
        }

        return Environments.FirstOrDefault(e => e.IsDefault)?.Name
            ?? Environments.FirstOrDefault()?.Name;
    }

    private EnvironmentConfig? FindEnvironmentByOriginalOrCurrentName(string? environmentName)
    {
        if (string.IsNullOrEmpty(environmentName))
        {
            return null;
        }

        return Environments.FirstOrDefault(env =>
            string.Equals(env.Name, environmentName, StringComparison.Ordinal) ||
            (_originalNames.TryGetValue(env, out var originalName) &&
             string.Equals(originalName, environmentName, StringComparison.Ordinal)));
    }

    private EnvironmentConfig CreateDraftEnvironment() => new()
    {
        Name = EditName.Trim(),
        GatewayUrl = EditUrl.Trim(),
        IsDefault = EditIsDefault,
    };

    private void ApplyChangedShellSettings(AppSettings settings)
    {
        if (_didEditSelectedLanguage)
        {
            settings.AppLanguage = SelectedLanguage;
        }

        if (_didEditMinimizeToTray)
        {
            settings.MinimizeToTray = MinimizeToTray;
        }

        if (_didEditCloseToTray)
        {
            settings.CloseToTray = CloseToTray;
        }

        if (_didEditAllowMultipleInstances)
        {
            settings.AllowMultipleInstances = AllowMultipleInstances;
        }

        if (_didEditEnableGlobalHotkey)
        {
            settings.EnableGlobalHotkey = EnableGlobalHotkey;
        }

        if (_didEditGlobalHotkey)
        {
            settings.GlobalHotkey = NormalizeHotkey(GlobalHotkey);
        }

        if (_didEditAlwaysOnTop)
        {
            settings.AlwaysOnTop = AlwaysOnTop;
        }

        if (_didEditEnableDevLog)
        {
            settings.Diagnostics.EnableVerboseRecoveryLogging = EnableDevLog;
        }

        if (_didEditEnableDevTools)
        {
            settings.Diagnostics.EnableDevTools = EnableDevTools;
        }
    }

    private bool HasEnvironmentMetadataChanges()
    {
        return Environments.Count != _originalSnapshots.Count ||
               Environments.Any(DidEnvironmentMetadataChange);
    }

    private bool HasEnvironmentSessionIdentityChanges()
    {
        return Environments.Count != _originalSnapshots.Count ||
               Environments.Any(DidEnvironmentSessionIdentityChange);
    }

    private static void EnsureAtLeastOneDefault(List<EnvironmentConfig> environments)
    {
        if (!environments.Any(e => e.IsDefault) &&
            environments.Count > 0)
        {
            environments[0].IsDefault = true;
        }
    }

    private static string NormalizeHotkey(string? hotkey)
    {
        return hotkey?.Trim() ?? string.Empty;
    }

    private bool DidEnvironmentSessionIdentityChange(EnvironmentConfig environment)
    {
        if (!_originalSnapshots.TryGetValue(environment, out var original))
        {
            return true;
        }

        return !string.Equals(original.Name, environment.Name, StringComparison.Ordinal) ||
               !string.Equals(original.GatewayUrl, environment.GatewayUrl, StringComparison.Ordinal);
    }

    private bool DidEnvironmentMetadataChange(EnvironmentConfig environment)
    {
        if (!_originalSnapshots.TryGetValue(environment, out var original))
        {
            return true;
        }

        return !string.Equals(original.Name, environment.Name, StringComparison.Ordinal) ||
               !string.Equals(original.GatewayUrl, environment.GatewayUrl, StringComparison.Ordinal) ||
               original.IsDefault != environment.IsDefault;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private static bool TryValidateEnvironment(EnvironmentConfig environment, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(environment.Name))
        {
            errorMessage = StringResources.SettingsValidationEnvironmentNameRequired;
            return false;
        }

        if (string.IsNullOrWhiteSpace(environment.GatewayUrl))
        {
            errorMessage = StringResources.SettingsValidationControlUiUrlRequired;
            return false;
        }

        if (!Uri.TryCreate(environment.GatewayUrl.Trim(), UriKind.Absolute, out var uri))
        {
            errorMessage = StringResources.SettingsValidationControlUiUrlAbsolute;
            return false;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            errorMessage = uri.Scheme is "ws" or "wss"
                ? StringResources.SettingsValidationControlUiUrlWs
                : StringResources.SettingsValidationControlUiUrlScheme;
            return false;
        }

        errorMessage = StringResources.SettingsValidationDefaultMessage;
        return true;
    }

    private bool TryValidateHotkey(out string errorMessage)
    {
        if (!EnableGlobalHotkey)
        {
            errorMessage = StringResources.SettingsValidationDefaultMessage;
            return true;
        }

        if (string.IsNullOrWhiteSpace(GlobalHotkey))
        {
            errorMessage = StringResources.SettingsValidationGlobalHotkeyRequired;
            return false;
        }

        var binding = HotkeyBinding.Parse(GlobalHotkey);
        if (binding is null || binding.GetVirtualKeyCode() == 0)
        {
            errorMessage = StringResources.SettingsValidationGlobalHotkeyInvalid;
            return false;
        }

        errorMessage = StringResources.SettingsValidationDefaultMessage;
        return true;
    }
}
