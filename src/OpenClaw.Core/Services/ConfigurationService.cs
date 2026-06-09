// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Text.Json;
using OpenClaw.Helpers;
using OpenClaw.Models;

namespace OpenClaw.Services;

/// <summary>
/// Manages application settings persistence using JSON file storage.
/// Settings are stored in %LOCALAPPDATA%\OpenClaw\settings.json.
/// </summary>
public class ConfigurationService
{
    private static readonly string DefaultAppDataFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenClaw");

    private readonly string _appDataFolder;
    private readonly string _settingsFilePath;
    private readonly IAppLogger _logger;
    private readonly TimeSpan _deferredSaveDelay;
    private readonly Action<string, string> _writeAllText;

    private readonly object _lock = new();
    private readonly object _deferredSaveGate = new();
    private CancellationTokenSource? _deferredSaveCts;
    private Task? _deferredSaveTask;
    private int _saveQueued;
    private int _saveVersion;
    private int _deferredSaveRequests;
    private int _deferredSaveCoalescedRequests;

    public ConfigurationService()
        : this(DefaultAppDataFolder, NullAppLogger.Instance)
    {
    }

    public ConfigurationService(IAppLogger? logger)
        : this(DefaultAppDataFolder, logger)
    {
    }

    public ConfigurationService(
        string appDataFolder,
        IAppLogger? logger = null,
        TimeSpan? deferredSaveDelay = null,
        Action<string, string>? writeAllText = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataFolder);
        _appDataFolder = Path.GetFullPath(appDataFolder);
        _settingsFilePath = Path.Combine(_appDataFolder, "settings.json");
        _logger = logger ?? NullAppLogger.Instance;
        _deferredSaveDelay = deferredSaveDelay ?? TimeSpan.FromMilliseconds(250);
        _writeAllText = writeAllText ?? AtomicFileWriter.WriteAllText;
    }

    /// <summary>
    /// Gets the current application settings.
    /// </summary>
    public AppSettings Settings { get; private set; } = new();

    /// <summary>
    /// Gets the full path to the settings JSON file.
    /// </summary>
    public string SettingsFilePath => _settingsFilePath;

    /// <summary>
    /// Gets the full path to the logs directory.
    /// </summary>
    public string LogsDirectory => Path.Combine(_appDataFolder, "logs");

    public int DeferredSaveRequests => Volatile.Read(ref _deferredSaveRequests);

    public int DeferredSaveCoalescedRequests => Volatile.Read(ref _deferredSaveCoalescedRequests);

    public string? LastLoadErrorMessage { get; private set; }

    /// <summary>
    /// Loads settings from disk. Creates defaults if the file doesn't exist.
    /// </summary>
    public void Load()
    {
        lock (_lock)
        {
            LastLoadErrorMessage = null;
            if (!File.Exists(_settingsFilePath))
            {
                LoadDefaultSettings(persistDefaults: true);
                return;
            }

            string json;
            try
            {
                json = File.ReadAllText(_settingsFilePath);
            }
            catch (Exception ex)
            {
                LastLoadErrorMessage = ex.Message;
                _logger.Error($"Failed to read settings: {ex.Message}");
                LoadDefaultSettings(persistDefaults: false);
                return;
            }

            try
            {
                var settings = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
                if (settings is null)
                {
                    throw new JsonException("Settings JSON deserialized to null.");
                }

                var settingsChanged = NormalizeSettings(settings, json);
                Settings = settings;
                if (settingsChanged)
                {
                    Save();
                }
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                LastLoadErrorMessage = ex.Message;
                _logger.Error($"Failed to parse settings: {ex.Message}");
                LoadDefaultSettings(persistDefaults: TryBackupInvalidSettingsFile());
            }
        }
    }

    private void LoadDefaultSettings(bool persistDefaults)
    {
        Settings = CreateDefaultSettings();
        NormalizeSettings(Settings);
        if (persistDefaults)
        {
            Save();
        }
    }

    private static AppSettings CreateDefaultSettings() => new()
    {
        Environments =
        [
            new EnvironmentConfig
            {
                Name = "Default",
                GatewayUrl = "https://example.com",
                IsDefault = true,
            }
        ],
        SelectedEnvironmentName = "Default",
    };

    private bool TryBackupInvalidSettingsFile()
    {
        try
        {
            Directory.CreateDirectory(_appDataFolder);
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var suffix = attempt == 0 ? string.Empty : $"-{attempt}";
                var backupPath = Path.Combine(
                    _appDataFolder,
                    $"settings.json.invalid-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}{suffix}.bak");
                if (File.Exists(backupPath))
                {
                    continue;
                }

                File.Copy(_settingsFilePath, backupPath, overwrite: false);
                _logger.Warning($"Backed up invalid settings to '{backupPath}'.");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to back up invalid settings: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Saves current settings to disk.
    /// </summary>
    public SettingsWriteResult Save()
    {
        lock (_lock)
        {
            return SaveCore(Settings, replaceCurrentSettings: false);
        }
    }

    public SettingsWriteResult Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_lock)
        {
            return SaveCore(settings, replaceCurrentSettings: !ReferenceEquals(Settings, settings));
        }
    }

    private SettingsWriteResult SaveCore(AppSettings settings, bool replaceCurrentSettings)
    {
        try
        {
            NormalizeSettings(settings);
            Directory.CreateDirectory(_appDataFolder);
            var json = JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);
            _writeAllText(_settingsFilePath, json);
            if (replaceCurrentSettings)
            {
                Settings = settings;
            }

            return SettingsWriteResult.Success();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to save settings: {ex.Message}");
            return SettingsWriteResult.Failure(ex.Message);
        }
    }

    public void SaveDeferred()
    {
        Interlocked.Increment(ref _deferredSaveRequests);

        if (!TryStartDeferredSaveWorker())
        {
            Interlocked.Increment(ref _deferredSaveCoalescedRequests);
            _logger.Info("settings.save_deferred.coalesced", new
            {
                requests = DeferredSaveRequests,
                coalesced = DeferredSaveCoalescedRequests
            });
            return;
        }

        _logger.Info("settings.save_deferred.queued", new
        {
            requests = DeferredSaveRequests,
            coalesced = DeferredSaveCoalescedRequests
        });
    }

    private bool TryStartDeferredSaveWorker()
    {
        var cancellation = new CancellationTokenSource();

        lock (_deferredSaveGate)
        {
            _saveVersion++;
            if (_deferredSaveTask is { IsCompleted: false })
            {
                cancellation.Dispose();
                return false;
            }

            _saveQueued = 1;
            _deferredSaveCts = cancellation;
            _deferredSaveTask = Task.Run(() => ProcessDeferredSaveQueueAsync(cancellation));
            return true;
        }
    }

    private async Task ProcessDeferredSaveQueueAsync(CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            while (true)
            {
                var versionToFlush = GetDeferredSaveVersion(cancellation);
                if (versionToFlush is null)
                {
                    return;
                }

                await Task.Delay(_deferredSaveDelay, token).ConfigureAwait(false);
                var saveResult = Save();
                _logger.Info("settings.save_deferred.flushed", new
                {
                    requests = DeferredSaveRequests,
                    coalesced = DeferredSaveCoalescedRequests,
                    saveResult.Succeeded
                });

                if (!saveResult.Succeeded)
                {
                    RetainDeferredSaveAfterFailure(cancellation);
                    return;
                }

                if (TryCompleteDeferredSaveBatch(cancellation, versionToFlush.Value))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Error($"Deferred settings save failed: {ex.Message}");
        }
        finally
        {
            CompleteDeferredSaveWorker(cancellation);
        }
    }

    private void RetainDeferredSaveAfterFailure(CancellationTokenSource cancellation)
    {
        lock (_deferredSaveGate)
        {
            if (!ReferenceEquals(_deferredSaveCts, cancellation))
            {
                return;
            }

            _saveQueued = 1;
            _deferredSaveCts = null;
            _deferredSaveTask = null;
        }

        cancellation.Dispose();
    }

    private int? GetDeferredSaveVersion(CancellationTokenSource cancellation)
    {
        lock (_deferredSaveGate)
        {
            if (!ReferenceEquals(_deferredSaveCts, cancellation))
            {
                return null;
            }

            return _saveVersion;
        }
    }

    private bool TryCompleteDeferredSaveBatch(CancellationTokenSource cancellation, int flushedVersion)
    {
        lock (_deferredSaveGate)
        {
            if (!ReferenceEquals(_deferredSaveCts, cancellation))
            {
                return true;
            }

            if (_saveVersion != flushedVersion)
            {
                return false;
            }

            _saveQueued = 0;
            _deferredSaveCts = null;
            _deferredSaveTask = null;
            cancellation.Dispose();
            return true;
        }
    }

    private void CompleteDeferredSaveWorker(CancellationTokenSource cancellation)
    {
        lock (_deferredSaveGate)
        {
            if (!ReferenceEquals(_deferredSaveCts, cancellation))
            {
                return;
            }

            _saveQueued = 0;
            _deferredSaveCts = null;
            _deferredSaveTask = null;
            cancellation.Dispose();
        }
    }

    private bool CancelDeferredSaveWorker()
    {
        var hadQueuedSave = false;
        CancellationTokenSource? cancellation;
        Task? task;

        lock (_deferredSaveGate)
        {
            hadQueuedSave = _saveQueued != 0 || _deferredSaveTask is not null;
            cancellation = _deferredSaveCts;
            task = _deferredSaveTask;
            _saveQueued = 0;
            _deferredSaveCts = null;
            _deferredSaveTask = null;
        }

        if (cancellation is null)
        {
            return hadQueuedSave;
        }

        cancellation.Cancel();

        var completed = true;
        if (task is not null)
        {
            try
            {
                completed = task.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                completed = true;
            }
        }

        if (completed)
        {
            cancellation.Dispose();
        }
        else if (task is not null)
        {
            _ = ObserveDeferredSaveWorkerShutdownAsync(task, cancellation);
        }

        return hadQueuedSave;
    }

    private static async Task ObserveDeferredSaveWorkerShutdownAsync(
        Task task,
        CancellationTokenSource cancellation)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // The worker already logs operational failures before it exits.
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    public void FlushDeferredSave()
    {
        var hadQueuedSave = CancelDeferredSaveWorker();

        if (!hadQueuedSave)
        {
            return;
        }

        var saveResult = Save();
        _logger.Info("settings.save_deferred.flush_on_shutdown", new
        {
            requests = DeferredSaveRequests,
            coalesced = DeferredSaveCoalescedRequests,
            saveResult.Succeeded
        });
    }

    /// <summary>
    /// Gets the default environment, or the first available, or null.
    /// </summary>
    public EnvironmentConfig? GetDefaultEnvironment()
    {
        return Settings.Environments.FirstOrDefault(e => e.IsDefault)
            ?? Settings.Environments.FirstOrDefault();
    }

    /// <summary>
    /// Gets the currently selected environment by persisted name.
    /// Falls back to default if the named environment is not found.
    /// </summary>
    public EnvironmentConfig? GetSelectedEnvironment()
    {
        if (!string.IsNullOrEmpty(Settings.SelectedEnvironmentName))
        {
            var env = Settings.Environments.FirstOrDefault(
                e => e.Name.Equals(Settings.SelectedEnvironmentName, StringComparison.Ordinal));
            if (env is not null)
            {
                return env;
            }
        }

        return GetDefaultEnvironment();
    }

    private static bool NormalizeSettings(AppSettings settings, string? rawJson = null)
    {
        var changed = false;

        if (settings.Environments is null)
        {
            settings.Environments = [];
            changed = true;
        }

        changed |= NormalizeEnvironments(settings);

        changed |= SetIfChanged(
            value => settings.AppTheme = value,
            settings.AppTheme,
            string.IsNullOrWhiteSpace(settings.AppTheme) ? "System" : settings.AppTheme.Trim());
        changed |= SetIfChanged(
            value => settings.AppLanguage = value,
            settings.AppLanguage,
            string.IsNullOrWhiteSpace(settings.AppLanguage) ? "System" : settings.AppLanguage.Trim());
        changed |= SetIfChanged(
            value => settings.GlobalHotkey = value,
            settings.GlobalHotkey,
            settings.GlobalHotkey?.Trim() ?? string.Empty);

        if (settings.RecoveryPolicy is null)
        {
            settings.RecoveryPolicy = new RecoveryPolicyOptions();
            changed = true;
        }

        if (settings.Heartbeat is null)
        {
            settings.Heartbeat = new HeartbeatOptions();
            changed = true;
        }

        if (settings.Diagnostics is null)
        {
            settings.Diagnostics = new DiagnosticsOptions();
            changed = true;
        }

        changed |= NormalizeWindowBounds(settings);
        changed |= NormalizeSettingsWindowBounds(settings);
        changed |= NormalizeRecoveryPolicy(settings.RecoveryPolicy);

        var normalizedHeartbeatInterval = Math.Max(0, settings.Heartbeat.IntervalSeconds);
        changed |= normalizedHeartbeatInterval != settings.Heartbeat.IntervalSeconds;
        settings.Heartbeat.IntervalSeconds = normalizedHeartbeatInterval;

        var normalizedHeartbeatFailureThreshold = Math.Max(1, settings.Heartbeat.FailureThreshold);
        changed |= normalizedHeartbeatFailureThreshold != settings.Heartbeat.FailureThreshold;
        settings.Heartbeat.FailureThreshold = normalizedHeartbeatFailureThreshold;

        var normalizedHeartbeatConnectingThreshold = Math.Max(1, settings.Heartbeat.ConnectingThreshold);
        changed |= normalizedHeartbeatConnectingThreshold != settings.Heartbeat.ConnectingThreshold;
        settings.Heartbeat.ConnectingThreshold = normalizedHeartbeatConnectingThreshold;

        var normalizedLegacyHeartbeatInterval = Math.Max(0, settings.HeartbeatIntervalSeconds);
        changed |= normalizedLegacyHeartbeatInterval != settings.HeartbeatIntervalSeconds;
        settings.HeartbeatIntervalSeconds = normalizedLegacyHeartbeatInterval;

        var hasLegacyInterval = false;
        var hasHeartbeatObject = false;
        var hasHeartbeatInterval = false;

        if (!string.IsNullOrWhiteSpace(rawJson))
        {
            try
            {
                using var document = JsonDocument.Parse(rawJson);
                var root = document.RootElement;
                hasLegacyInterval = root.TryGetProperty("heartbeatIntervalSeconds", out _);

                if (root.TryGetProperty("heartbeat", out var heartbeatElement) &&
                    heartbeatElement.ValueKind == JsonValueKind.Object)
                {
                    hasHeartbeatObject = true;
                    hasHeartbeatInterval = heartbeatElement.TryGetProperty("intervalSeconds", out _);
                }
            }
            catch (JsonException)
            {
                // Deserialization already succeeded; leave normalization on the object graph only.
            }
        }

        if (hasLegacyInterval && (!hasHeartbeatObject || !hasHeartbeatInterval))
        {
            changed |= settings.Heartbeat.IntervalSeconds != settings.HeartbeatIntervalSeconds;
            changed |= settings.Heartbeat.EnableHeartbeat != (settings.HeartbeatIntervalSeconds > 0);
            settings.Heartbeat.IntervalSeconds = settings.HeartbeatIntervalSeconds;
            settings.Heartbeat.EnableHeartbeat = settings.HeartbeatIntervalSeconds > 0;
        }

        var synchronizedHeartbeatInterval = settings.Heartbeat.EnableHeartbeat
            ? settings.Heartbeat.IntervalSeconds
            : 0;
        changed |= settings.HeartbeatIntervalSeconds != synchronizedHeartbeatInterval;
        settings.HeartbeatIntervalSeconds = synchronizedHeartbeatInterval;

        return changed;
    }

    private static bool NormalizeEnvironments(AppSettings settings)
    {
        var changed = false;
        var source = settings.Environments;
        var normalized = new List<EnvironmentConfig>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectedEnvironmentName = settings.SelectedEnvironmentName?.Trim();
        EnvironmentConfig? selectedEnvironmentExact = null;
        EnvironmentConfig? selectedEnvironmentCaseFallback = null;

        foreach (var environment in source)
        {
            if (environment is null)
            {
                changed = true;
                continue;
            }

            var name = environment.Name?.Trim() ?? string.Empty;
            var gatewayUrl = environment.GatewayUrl?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(gatewayUrl) ||
                !GatewayUrlIdentity.IsSupportedGatewayUrl(gatewayUrl))
            {
                changed = true;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(selectedEnvironmentName))
            {
                if (selectedEnvironmentExact is null &&
                    string.Equals(name, selectedEnvironmentName, StringComparison.Ordinal))
                {
                    selectedEnvironmentExact = environment;
                }

                if (selectedEnvironmentCaseFallback is null &&
                    string.Equals(name, selectedEnvironmentName, StringComparison.OrdinalIgnoreCase))
                {
                    selectedEnvironmentCaseFallback = environment;
                }
            }

            var uniqueName = name;
            if (!usedNames.Add(uniqueName))
            {
                var suffix = 2;
                do
                {
                    uniqueName = $"{name} ({suffix++})";
                }
                while (!usedNames.Add(uniqueName));

                changed = true;
            }

            if (!string.Equals(environment.Name, uniqueName, StringComparison.Ordinal))
            {
                environment.Name = uniqueName;
                changed = true;
            }

            if (!string.Equals(environment.GatewayUrl, gatewayUrl, StringComparison.Ordinal))
            {
                environment.GatewayUrl = gatewayUrl;
                changed = true;
            }

            normalized.Add(environment);
        }

        if (normalized.Count == 0)
        {
            normalized.Add(new EnvironmentConfig
            {
                Name = "Default",
                GatewayUrl = EnvironmentConfig.PlaceholderGatewayUrl,
                IsDefault = true,
            });
            changed = true;
        }

        if (!string.Equals(settings.SelectedEnvironmentName, selectedEnvironmentName, StringComparison.Ordinal))
        {
            settings.SelectedEnvironmentName = selectedEnvironmentName;
            changed = true;
        }

        var selectedEnvironment = selectedEnvironmentExact ?? selectedEnvironmentCaseFallback;

        var defaultEnvironment = normalized.FirstOrDefault(env => env.IsDefault)
            ?? selectedEnvironment
            ?? normalized[0];

        var defaultAssigned = false;
        foreach (var environment in normalized)
        {
            var shouldBeDefault = ReferenceEquals(environment, defaultEnvironment) && !defaultAssigned;
            if (environment.IsDefault != shouldBeDefault)
            {
                environment.IsDefault = shouldBeDefault;
                changed = true;
            }

            defaultAssigned |= shouldBeDefault;
        }

        if (selectedEnvironment is null)
        {
            settings.SelectedEnvironmentName = defaultEnvironment.Name;
            changed = true;
        }
        else if (!string.Equals(settings.SelectedEnvironmentName, selectedEnvironment.Name, StringComparison.Ordinal))
        {
            settings.SelectedEnvironmentName = selectedEnvironment.Name;
            changed = true;
        }

        if (source.Count != normalized.Count || !source.SequenceEqual(normalized))
        {
            changed = true;
        }

        settings.Environments = normalized;
        return changed;
    }

    private static bool NormalizeRecoveryPolicy(RecoveryPolicyOptions recovery)
    {
        var changed = false;

        changed |= SetIfChanged(
            value => recovery.BackgroundResumeThresholdSeconds = value,
            recovery.BackgroundResumeThresholdSeconds,
            Math.Max(0, recovery.BackgroundResumeThresholdSeconds));
        changed |= SetIfChanged(
            value => recovery.MaxReconnectAttempts = value,
            recovery.MaxReconnectAttempts,
            Math.Max(1, recovery.MaxReconnectAttempts));
        changed |= SetIfChanged(
            value => recovery.MaxSoftResyncAttempts = value,
            recovery.MaxSoftResyncAttempts,
            Math.Max(1, recovery.MaxSoftResyncAttempts));
        changed |= SetIfChanged(
            value => recovery.EventIdleSuspicionSeconds = value,
            recovery.EventIdleSuspicionSeconds,
            Math.Max(0, recovery.EventIdleSuspicionSeconds));
        changed |= SetIfChanged(
            value => recovery.TransportIdleSuspicionSeconds = value,
            recovery.TransportIdleSuspicionSeconds,
            Math.Max(0, recovery.TransportIdleSuspicionSeconds));
        changed |= SetIfChanged(
            value => recovery.ReconnectDelayMs = value,
            recovery.ReconnectDelayMs,
            Math.Max(0, recovery.ReconnectDelayMs));

        var normalizedBackoff = double.IsFinite(recovery.ReconnectBackoffMultiplier) &&
            recovery.ReconnectBackoffMultiplier >= 1d
            ? recovery.ReconnectBackoffMultiplier
            : 1d;
        if (!recovery.ReconnectBackoffMultiplier.Equals(normalizedBackoff))
        {
            recovery.ReconnectBackoffMultiplier = normalizedBackoff;
            changed = true;
        }

        changed |= SetIfChanged(
            value => recovery.MaxReconnectDelayMs = value,
            recovery.MaxReconnectDelayMs,
            Math.Max(recovery.ReconnectDelayMs, recovery.MaxReconnectDelayMs));
        changed |= SetIfChanged(
            value => recovery.HardRefreshCooldownSeconds = value,
            recovery.HardRefreshCooldownSeconds,
            Math.Max(0, recovery.HardRefreshCooldownSeconds));

        return changed;
    }

    private static bool SetIfChanged(Action<int> setValue, int current, int normalized)
    {
        if (current == normalized)
        {
            return false;
        }

        setValue(normalized);
        return true;
    }

    private static bool SetIfChanged(Action<string> setValue, string? current, string normalized)
    {
        if (string.Equals(current, normalized, StringComparison.Ordinal))
        {
            return false;
        }

        setValue(normalized);
        return true;
    }

    private static bool NormalizeWindowBounds(AppSettings settings)
    {
        if (!WindowBoundsUtilities.HasPersistableSize(settings.WindowWidth, settings.WindowHeight) ||
            WindowBoundsUtilities.HasMinimizedSentinelPosition(settings.WindowLeft, settings.WindowTop))
        {
            settings.WindowWidth = WindowBoundsUtilities.DefaultWindowWidth;
            settings.WindowHeight = WindowBoundsUtilities.DefaultWindowHeight;
            settings.WindowLeft = -1;
            settings.WindowTop = -1;
            return true;
        }

        if (!WindowBoundsUtilities.HasSavedPosition(settings.WindowLeft, settings.WindowTop) &&
            (settings.WindowLeft != -1 || settings.WindowTop != -1))
        {
            settings.WindowLeft = -1;
            settings.WindowTop = -1;
            return true;
        }

        return false;
    }

    private static bool NormalizeSettingsWindowBounds(AppSettings settings)
    {
        if (!WindowBoundsUtilities.HasPersistableSize(
                settings.SettingsWindowWidth,
                settings.SettingsWindowHeight,
                WindowBoundsUtilities.MinimumPersistedSettingsWindowWidth,
                WindowBoundsUtilities.MinimumPersistedSettingsWindowHeight) ||
            WindowBoundsUtilities.HasMinimizedSentinelPosition(settings.SettingsWindowLeft, settings.SettingsWindowTop))
        {
            settings.SettingsWindowWidth = WindowBoundsUtilities.DefaultSettingsWindowWidth;
            settings.SettingsWindowHeight = WindowBoundsUtilities.DefaultSettingsWindowHeight;
            settings.SettingsWindowLeft = -1;
            settings.SettingsWindowTop = -1;
            return true;
        }

        if (!WindowBoundsUtilities.HasSavedPosition(settings.SettingsWindowLeft, settings.SettingsWindowTop) &&
            (settings.SettingsWindowLeft != -1 || settings.SettingsWindowTop != -1))
        {
            settings.SettingsWindowLeft = -1;
            settings.SettingsWindowTop = -1;
            return true;
        }

        return false;
    }
}
