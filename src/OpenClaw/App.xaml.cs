// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Xaml;
using OpenClaw.Helpers;
using OpenClaw.Services;

namespace OpenClaw;

/// <summary>
/// The application entry point. Initializes services and creates the main window.
/// </summary>
public partial class App : Application
{
    private Window? _mainWindow;
    private SingleInstanceCoordinator? _singleInstanceCoordinator;
    private readonly SemaphoreSlim _singleInstancePreferenceGate = new(1, 1);
    private int _isShuttingDown;

    static App()
    {
        OpenClaw.Services.AppTelemetry.DeferredSaveRequestsProvider = () => Configuration.DeferredSaveRequests;
        OpenClaw.Services.AppTelemetry.DeferredSaveCoalescedRequestsProvider = () => Configuration.DeferredSaveCoalescedRequests;
    }

    public App()
    {
        this.InitializeComponent();
        this.UnhandledException += OnUnhandledException;
    }

    /// <summary>
    /// Gets the singleton <see cref="LoggingService"/> instance.
    /// </summary>
    public static LoggingService Logger { get; } = new();

    /// <summary>
    /// Gets the singleton <see cref="ConfigurationService"/> instance.
    /// </summary>
    public static ConfigurationService Configuration { get; } = new(Logger);

    /// <summary>
    /// Gets the main application window.
    /// </summary>
    public static Window? MainWindow { get; private set; }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            await LaunchAsync(args);
        }
        catch (Exception ex)
        {
            Logger.Error($"Application launch failed: {ex}");
            Logger.Dispose();
            Exit();
        }
    }

    private async Task LaunchAsync(LaunchActivatedEventArgs args)
    {
        Logger.Info("Application launching.");
        Configuration.Load();
        Logger.Info("Configuration loaded.");

        if (!Configuration.Settings.AllowMultipleInstances && await RedirectSecondaryLaunchToPrimaryInstanceAsync())
        {
            Logger.Dispose();
            Exit();
            return;
        }

        // Apply saved language preference
        if (ApplyLanguage(Configuration.Settings.AppLanguage))
        {
            Logger.Info("Language applied.");
        }

        Logger.Info("Creating main window.");
        _mainWindow = new MainWindow();
        _mainWindow.Closed += OnMainWindowClosed;
        Logger.Info("Main window created.");
        MainWindow = _mainWindow;
        _mainWindow.Activate();

        Logger.Info("Main window activated.");
    }

    private async Task<bool> RedirectSecondaryLaunchToPrimaryInstanceAsync()
    {
        _singleInstanceCoordinator = SingleInstanceCoordinator.CreatePrimaryOrSecondary(Logger);
        if (_singleInstanceCoordinator.IsPrimary)
        {
            _singleInstanceCoordinator.ActivationRequested += OnPrimaryInstanceActivationRequested;
            _singleInstanceCoordinator.StartListening();
            return false;
        }

        Logger.Info("Secondary launch detected; requesting primary instance activation.");
        if (await SingleInstanceCoordinator.RequestActivationOfPrimaryInstanceAsync(Logger))
        {
            _singleInstanceCoordinator.Dispose();
            _singleInstanceCoordinator = null;
            return true;
        }

        Logger.Warning("Primary instance activation failed; waiting briefly to take over single-instance ownership.");
        _singleInstanceCoordinator.Dispose();
        _singleInstanceCoordinator = null;
        _singleInstanceCoordinator = await SingleInstanceCoordinator.TryCreatePrimaryAfterActivationFailureAsync(Logger);
        if (_singleInstanceCoordinator is null)
        {
            return true;
        }

        Logger.Info("Recovered single-instance ownership after activation failure.");
        _singleInstanceCoordinator.ActivationRequested += OnPrimaryInstanceActivationRequested;
        _singleInstanceCoordinator.StartListening();
        return false;
    }

    internal void ApplySingleInstancePreference(bool allowMultipleInstances)
    {
        _ = ObserveSingleInstancePreferenceChangeAsync(allowMultipleInstances);
    }

    private async Task ObserveSingleInstancePreferenceChangeAsync(bool allowMultipleInstances)
    {
        try
        {
            await ApplySingleInstancePreferenceAsync(allowMultipleInstances).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Warning($"Single-instance preference update failed: {ex.Message}");
        }
    }

    private async Task ApplySingleInstancePreferenceAsync(bool allowMultipleInstances)
    {
        await _singleInstancePreferenceGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _isShuttingDown) != 0)
            {
                return;
            }

            await ApplySingleInstancePreferenceCoreAsync(allowMultipleInstances).ConfigureAwait(false);
        }
        finally
        {
            _singleInstancePreferenceGate.Release();
        }
    }

    private async Task ApplySingleInstancePreferenceCoreAsync(bool allowMultipleInstances)
    {
        if (allowMultipleInstances)
        {
            await StopSingleInstanceCoordinatorAsync().ConfigureAwait(false);
            return;
        }

        if (_singleInstanceCoordinator is not null)
        {
            return;
        }

        _singleInstanceCoordinator = SingleInstanceCoordinator.CreatePrimaryOrSecondary(Logger);
        if (_singleInstanceCoordinator.IsPrimary)
        {
            _singleInstanceCoordinator.ActivationRequested += OnPrimaryInstanceActivationRequested;
            _singleInstanceCoordinator.StartListening();
            Logger.Info("Single-instance coordination enabled for future launches.");
            return;
        }

        Logger.Warning("Single-instance coordination could not be enabled because another instance already owns it.");
        _singleInstanceCoordinator.Dispose();
        _singleInstanceCoordinator = null;
    }

    private async Task StopSingleInstanceCoordinatorAsync()
    {
        var coordinator = DetachSingleInstanceCoordinator();
        if (coordinator is null)
        {
            return;
        }

        try
        {
            await coordinator.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Warning($"Single-instance listener shutdown failed: {ex.Message}");
        }
        finally
        {
            coordinator.Dispose();
            Logger.Info("Single-instance coordination disabled for future launches.");
        }
    }

    private void StopSingleInstanceCoordinator()
    {
        SingleInstanceCoordinator? coordinator = null;
        _singleInstancePreferenceGate.Wait();
        try
        {
            coordinator = DetachSingleInstanceCoordinator();
        }
        finally
        {
            _singleInstancePreferenceGate.Release();
        }

        if (coordinator is null)
        {
            return;
        }

        try
        {
            coordinator.StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Logger.Warning($"Single-instance listener shutdown failed: {ex.Message}");
        }
        finally
        {
            coordinator.Dispose();
            Logger.Info("Single-instance coordination disabled for future launches.");
        }
    }

    private SingleInstanceCoordinator? DetachSingleInstanceCoordinator()
    {
        var coordinator = _singleInstanceCoordinator;
        _singleInstanceCoordinator = null;
        if (coordinator is not null)
        {
            coordinator.ActivationRequested -= OnPrimaryInstanceActivationRequested;
        }

        return coordinator;
    }

    private void OnPrimaryInstanceActivationRequested()
    {
        Logger.Info("Primary instance activation requested.");
        if (_mainWindow is not MainWindow mainWindow)
        {
            return;
        }

        mainWindow.DispatcherQueue.TryEnqueue(mainWindow.ActivateFromExternalLaunch);
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        Volatile.Write(ref _isShuttingDown, 1);

        if (_mainWindow is not null)
        {
            _mainWindow.Closed -= OnMainWindowClosed;
        }

        StopSingleInstanceCoordinator();
        Logger.Dispose();
    }

    /// <summary>
    /// Applies the language override. "System" skips override to follow OS language.
    /// </summary>
    public static bool ApplyLanguage(string language)
    {
        try
        {
            if (string.IsNullOrEmpty(language) || language == "System")
            {
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = string.Empty;
            }
            else
            {
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Language override failed: {ex.Message}");
            return false;
        }
        finally
        {
            StringResources.Invalidate();
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Logger.Error($"Unhandled exception: {e.Exception}");
    }
}
