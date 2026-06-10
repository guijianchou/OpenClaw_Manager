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

    /// <summary>
    /// Loads settings from disk. Creates defaults if the file doesn't exist.
    /// </summary>
    public void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
                    if (settings is not null)
                    {
                        var settingsChanged = NormalizeSettings(settings, json);
                        Settings = settings;
                        if (settingsChanged)
                        {
                            Save();
                        }

                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load settings: {ex.Message}");
            }

            // Create default settings with a sample environment
            Settings = new AppSettings
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
            NormalizeSettings(Settings);
            Save();
        }
    }

    /// <summary>
    /// Saves current settings to disk.
    /// </summary>
    public SettingsWriteResult Save()
    {
        lock (_lock)
        {
            try
            {
                NormalizeSettings(Settings);
                Directory.CreateDirectory(_appDataFolder);
                var json = JsonSerializer.Serialize(Settings, AppSettingsJsonContext.Default.AppSettings);
                _writeAllText(_settingsFilePath, json);
                return SettingsWriteResult.Success();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to save settings: {ex.Message}");
                return SettingsWriteResult.Failure(ex.Message);
            }
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
