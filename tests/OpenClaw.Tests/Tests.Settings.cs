using System.Net;
using System.Reflection;
using OpenClaw.Helpers;
using OpenClaw.Models;
using OpenClaw.Services;

internal static partial class Tests
{
    public static async Task DeferredSaveFlushesRequestsQueuedDuringWriteAsync()
    {
        var directory = CreateTempDirectory();
        try
        {
            var writeCount = 0;
            var firstWriteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            void WriteSettings(string path, string contents)
            {
                var count = Interlocked.Increment(ref writeCount);
                if (count == 1)
                {
                    firstWriteStarted.SetResult();
                    releaseFirstWrite.Task.GetAwaiter().GetResult();
                }

                File.WriteAllText(path, contents);
            }

            var configuration = new ConfigurationService(
                directory,
                new TestLogger(),
                TimeSpan.Zero,
                WriteSettings);

            configuration.Settings.AppLanguage = "en-US";
            configuration.SaveDeferred();

            await firstWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            configuration.Settings.AppLanguage = "zh-CN";
            configuration.SaveDeferred();
            releaseFirstWrite.SetResult();

            await WaitUntilAsync(() => Volatile.Read(ref writeCount) >= 2, TimeSpan.FromSeconds(5));

            var json = File.ReadAllText(Path.Combine(directory, "settings.json"));
            Assert.Contains("\"appLanguage\": \"zh-CN\"", json, "Deferred save should flush changes queued while an earlier save is writing.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static Task SettingsLoadNormalizesNullOptionSections()
    {
        var directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "settings.json"),
                """
                {
                  "environments": null,
                  "heartbeat": null,
                  "recoveryPolicy": null,
                  "diagnostics": null
                }
                """);

            var configuration = new ConfigurationService(directory, new TestLogger(), TimeSpan.Zero);

            configuration.Load();

            Assert.NotNull(configuration.Settings.Environments, "NormalizeSettings should repair a null environments array.");
            Assert.NotNull(configuration.Settings.Heartbeat, "NormalizeSettings should repair null heartbeat options.");
            Assert.NotNull(configuration.Settings.RecoveryPolicy, "NormalizeSettings should repair null recovery policy options.");
            Assert.NotNull(configuration.Settings.Diagnostics, "NormalizeSettings should repair null diagnostics options.");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static async Task LatencyProbeRequestsControlUiConfigAsync()
    {
        Uri? requestedUri = null;
        using var service = new ControlUiLatencyService(
            new StubHttpMessageHandler((request, _) =>
            {
                requestedUri = request.RequestUri;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }),
            TimeSpan.FromMilliseconds(50));

        var snapshotSource = new TaskCompletionSource<ControlUiLatencySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.LatencyUpdated += snapshot => snapshotSource.TrySetResult(snapshot);

        service.Start("https://ai.falsemeet.site/control/");
        var snapshot = await snapshotSource.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("/control/__openclaw/control-ui-config.json", requestedUri?.AbsolutePath, "Latency probe should target the hosted Control UI config endpoint under the configured base path.");
        Assert.True(snapshot.IsSuccess, "401 from the config endpoint should still prove the Gateway is reachable.");
    }

    public static async Task LatencyProbeCancellationCompletesBackgroundTaskAsync()
    {
        using var service = new ControlUiLatencyService(
            new StubHttpMessageHandler(async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            TimeSpan.FromSeconds(30));

        service.Start("https://gateway.example/");
        service.Stop();

        var probeTask = GetCurrentProbeTask(service);
        Assert.NotNull(probeTask, "Latency service should keep the active probe task observable for shutdown.");
        await probeTask!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(probeTask.IsCompletedSuccessfully, "Cancelling the initial latency probe should complete the background task without faulting.");
    }

    public static Task LatencyHistoryTooltipSummarizesRecentSamples()
    {
        var history = new LatencyHistory(capacity: 3);

        history.Record(ControlUiLatencySnapshot.Success("gateway.example", 90));
        history.Record(ControlUiLatencySnapshot.Success("gateway.example", 120));
        history.Record(ControlUiLatencySnapshot.Unknown);
        history.Record(ControlUiLatencySnapshot.Success("gateway.example", 250));
        history.Record(ControlUiLatencySnapshot.Success("gateway.example", 180));

        var summary = history.CreateSummary();

        Assert.Equal(3, summary.SampleCount, "Latency history should keep only the configured number of recent successful samples.");
        Assert.Equal(180L, summary.LatestMs, "Latency history should report the latest successful sample.");
        Assert.Equal(120L, summary.MinMs, "Latency history should report the minimum retained sample.");
        Assert.Equal(183L, summary.AverageMs, "Latency history should round the average retained sample.");
        Assert.Equal(250L, summary.P95Ms, "Latency history should use nearest-rank p95 for the retained samples.");
        Assert.Equal(250L, summary.MaxMs, "Latency history should report the maximum retained sample.");
        Assert.Equal(
            "Latency history (3 samples)\nLatest: 180 ms\nMin: 120 ms\nAvg: 183 ms\nP95: 250 ms\nMax: 250 ms",
            LatencyTooltipFormatter.Format(summary),
            "Latency tooltip should expose the operational min/avg/p95/max values.");
        return Task.CompletedTask;
    }

    public static Task TrayClosePolicyHidesToTrayUntilExitRequested()
    {
        var policy = new TrayClosePolicy();

        Assert.Equal(TrayCloseDisposition.HideToTray, policy.GetCloseDisposition(closeToTray: true), "Normal close should hide the window to tray.");

        policy.RequestExit();

        Assert.Equal(TrayCloseDisposition.Exit, policy.GetCloseDisposition(closeToTray: true), "Explicit quit should allow the app to exit.");
        return Task.CompletedTask;
    }

    public static Task TrayClosePolicyRespectsCloseToTraySetting()
    {
        var policy = new TrayClosePolicy();

        Assert.Equal(TrayCloseDisposition.Exit, policy.GetCloseDisposition(closeToTray: false), "Disabling close-to-tray should let the close button exit.");
        return Task.CompletedTask;
    }

    public static Task SettingsLoadDefaultsTrayOptionsOn()
    {
        var settings = new AppSettings();

        Assert.True(settings.MinimizeToTray, "Minimize-to-tray should default on so the shell behaves like a tray app.");
        Assert.True(settings.CloseToTray, "Close-to-tray should default on so the window close button keeps the tray app alive.");
        return Task.CompletedTask;
    }

    public static Task SettingsLoadRejectsMinimizedWindowSentinelBounds()
    {
        var directory = CreateTempDirectory();
        try
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            File.WriteAllText(settingsPath, """
            {
              "windowWidth": 160,
              "windowHeight": 28,
              "windowLeft": -32000,
              "windowTop": -32000
            }
            """);

            var configuration = new ConfigurationService(directory, new TestLogger());
            configuration.Load();

            Assert.Equal(1280d, configuration.Settings.WindowWidth, "Minimized sentinel width should reset to the default window width.");
            Assert.Equal(800d, configuration.Settings.WindowHeight, "Minimized sentinel height should reset to the default window height.");
            Assert.Equal(-1d, configuration.Settings.WindowLeft, "Minimized sentinel left should reset to the unset window position.");
            Assert.Equal(-1d, configuration.Settings.WindowTop, "Minimized sentinel top should reset to the unset window position.");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static Task MainWindowSkipsPersistingMinimizedOrHiddenBounds()
    {
        var lifecyclePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "MainWindow.Lifecycle.cs");
        var source = File.ReadAllText(lifecyclePath);
        var saveIndex = source.IndexOf("private void SaveWindowBounds()", StringComparison.Ordinal);
        var closeIndex = source.IndexOf("private void OnWindowClosed", StringComparison.Ordinal);

        Assert.True(saveIndex >= 0, "SaveWindowBounds should exist.");
        Assert.True(closeIndex > saveIndex, "SaveWindowBounds should appear before OnWindowClosed.");

        var saveMethod = source[saveIndex..closeIndex];
        Assert.Contains("_isWindowHidden", saveMethod, "Hidden-to-tray windows should not overwrite the last visible bounds.");
        Assert.Contains("WindowFrameHelper.IsWindowMinimized(this)", saveMethod, "Minimized windows should not overwrite the last visible bounds.");
        return Task.CompletedTask;
    }

    public static Task SettingsDefaultDisablesMultipleInstances()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw.Core",
            "Models",
            "AppSettings.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("AllowMultipleInstances", source, "Settings should persist whether multiple OpenClaw windows are allowed.");
        Assert.Contains("AllowMultipleInstances { get; set; } = false", source, "Multiple instances should be disabled by default.");
        return Task.CompletedTask;
    }

    public static Task SettingsGeneralExposesMultipleInstancesOption()
    {
        var xamlPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.xaml");
        var viewModelPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "ViewModels",
            "SettingsViewModel.cs");
        var enResourcesPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Strings",
            "en-us",
            "Resources.resw");

        var xaml = File.ReadAllText(xamlPath);
        var viewModel = File.ReadAllText(viewModelPath);
        var enResources = File.ReadAllText(enResourcesPath);

        Assert.Contains("SettingsAllowMultipleInstances", xaml, "General settings should include a Multiple instances settings row.");
        Assert.Contains("AllowMultipleInstances", viewModel, "SettingsViewModel should expose the multiple instances setting.");
        Assert.Contains("Settings.AllowMultipleInstances = AllowMultipleInstances", viewModel, "Settings save should persist the multiple instances setting.");
        Assert.Contains("<value>Multiple instances</value>", enResources, "English label should be exactly Multiple instances.");
        Assert.Contains("<value>Allow multiple instance of Openclaw for windows</value>", enResources, "English description should match the requested wording.");
        return Task.CompletedTask;
    }

    public static Task SettingsBooleanOptionsUsePowerToysStyleRows()
    {
        var xamlPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.xaml");
        var projectPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "OpenClaw.csproj");

        var xaml = File.ReadAllText(xamlPath);
        var project = File.ReadAllText(projectPath);

        Assert.Contains("CommunityToolkit.WinUI.Controls", project, "OpenClaw should reference the Toolkit settings controls used by SettingsCard.");
        Assert.Contains("xmlns:controls=\"using:CommunityToolkit.WinUI.Controls\"", xaml, "Settings XAML should import Toolkit controls.");
        Assert.Contains("<controls:SettingsCard", xaml, "Boolean settings should be grouped in SettingsCard rows.");
        Assert.Contains("<ToggleSwitch", xaml, "Boolean settings should use right-aligned ToggleSwitch controls.");
        Assert.DoesNotContain("<CheckBox", xaml, "Settings should not use checkbox controls after adopting the PowerToys settings row style.");

        Assert.Contains("IsOn=\"{x:Bind ViewModel.MinimizeToTray, Mode=TwoWay}\"", xaml, "Minimize-to-tray should bind through ToggleSwitch.IsOn.");
        Assert.Contains("IsOn=\"{x:Bind ViewModel.CloseToTray, Mode=TwoWay}\"", xaml, "Close-to-tray should bind through ToggleSwitch.IsOn.");
        Assert.Contains("IsOn=\"{x:Bind ViewModel.AllowMultipleInstances, Mode=TwoWay}\"", xaml, "Multiple instances should bind through ToggleSwitch.IsOn.");
        Assert.Contains("IsOn=\"{x:Bind ViewModel.EnableGlobalHotkey, Mode=TwoWay}\"", xaml, "Global hotkey enable should bind through ToggleSwitch.IsOn.");
        Assert.Contains("IsOn=\"{x:Bind ViewModel.AlwaysOnTop, Mode=TwoWay}\"", xaml, "Always-on-top should bind through ToggleSwitch.IsOn.");
        Assert.Contains("IsOn=\"{x:Bind ViewModel.EditIsDefault, Mode=TwoWay}\"", xaml, "Default environment should bind through ToggleSwitch.IsOn.");
        Assert.Contains("IsOn=\"{x:Bind ViewModel.EnableDevLog, Mode=TwoWay}\"", xaml, "Developer logging should bind through ToggleSwitch.IsOn.");
        return Task.CompletedTask;
    }

    public static Task SettingsEnvironmentEditKeepsApplyAction()
    {
        var xamlPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.xaml");
        var actionsPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.Actions.cs");
        var resourcesPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Helpers",
            "StringResources.cs");

        var xaml = File.ReadAllText(xamlPath);
        var actions = File.ReadAllText(actionsPath);
        var resources = File.ReadAllText(resourcesPath);
        var actionsBarIndex = xaml.IndexOf("x:Name=\"EnvironmentActionsBar\"", StringComparison.Ordinal);
        var defaultIndex = xaml.IndexOf("x:Name=\"DefaultEnvironmentRow\"", StringComparison.Ordinal);
        var applyIndex = xaml.IndexOf("Click=\"OnApplyClick\"", StringComparison.Ordinal);

        Assert.True(actionsBarIndex >= 0, "Environment default toggle and Apply action should share one cohesive action bar.");
        Assert.True(defaultIndex >= 0, "Environment default toggle should live in a named compact row.");
        Assert.True(actionsBarIndex < defaultIndex, "Default toggle should sit inside the environment action bar.");
        Assert.True(applyIndex > defaultIndex, "Environment Apply should sit to the right of the default toggle inside the action bar.");
        Assert.Contains("ColumnSpacing=\"16\"", xaml, "Environment action bar should use compact two-column spacing.");
        Assert.Contains("SettingsApply", xaml, "Environment Apply button should use the localized SettingsApply label.");
        Assert.Contains("private void OnApplyClick", actions, "Settings should expose an explicit Apply handler for environment edits.");
        Assert.Contains("TryApplyEdit()", actions, "Apply handler should commit the current environment draft before switching or saving.");
        Assert.Contains("ValidationInfoBar.IsOpen = false;", actions, "Apply handler should clear stale validation errors after a successful environment edit.");
        Assert.Contains("public static string SettingsApply", resources, "SettingsApply should be exposed through StringResources.");
        return Task.CompletedTask;
    }

    public static Task SettingsAlwaysOnTopStringsAreLocalized()
    {
        var xamlPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.xaml");
        var resourcesPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Helpers",
            "StringResources.cs");
        var enResourcesPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Strings",
            "en-us",
            "Resources.resw");
        var zhResourcesPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Strings",
            "zh-cn",
            "Resources.resw");

        var xaml = File.ReadAllText(xamlPath);
        var resources = File.ReadAllText(resourcesPath);
        var enResources = File.ReadAllText(enResourcesPath);
        var zhResources = File.ReadAllText(zhResourcesPath);

        Assert.Contains("helpers:StringResources.SettingsAlwaysOnTop", xaml, "Always-on-top header should use localized resources.");
        Assert.Contains("helpers:StringResources.SettingsAlwaysOnTopDescription", xaml, "Always-on-top description should use localized resources.");
        Assert.DoesNotContain("Header=\"Always on Top\"", xaml, "Always-on-top should not hard-code English in XAML.");
        Assert.Contains("public static string SettingsAlwaysOnTop", resources, "Always-on-top label should be exposed through StringResources.");
        Assert.Contains("SettingsAlwaysOnTop", enResources, "English resources should include the always-on-top label.");
        Assert.Contains("SettingsAlwaysOnTop", zhResources, "Chinese resources should include the always-on-top label.");
        return Task.CompletedTask;
    }

    public static Task SettingsSwitchRowsUseCompactSpacing()
    {
        var xamlPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.xaml");

        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("x:Name=\"ShellSwitchRows\"", xaml, "Shell switch cards should be grouped so their spacing is intentional.");
        Assert.Contains("x:Name=\"ShellSwitchRows\" Spacing=\"8\"", xaml, "Shell switch cards should use compact 8px spacing.");
        Assert.Contains("x:Name=\"DevToolsSwitchRows\" Spacing=\"8\"", xaml, "Developer switch cards should use compact 8px spacing.");
        Assert.DoesNotContain("<controls:SettingsCard Header=\"{x:Bind helpers:StringResources.SetAsDefault}\"", xaml, "Set as default should not use the full-height SettingsCard treatment.");
        return Task.CompletedTask;
    }

    public static Task AppStartupHonorsMultipleInstanceSetting()
    {
        var appPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "App.xaml.cs");
        var coordinatorPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw.Core",
            "Services",
            "SingleInstanceCoordinator.cs");
        var appSource = File.ReadAllText(appPath);
        var coordinatorSource = File.ReadAllText(coordinatorPath);

        Assert.True(File.Exists(coordinatorPath), "Single-instance startup should live in a dedicated coordinator service.");
        Assert.Contains("public sealed class SingleInstanceCoordinator", coordinatorSource, "Single-instance coordinator should be public so the WinUI app can use the Core service.");
        Assert.Contains("AllowMultipleInstances", appSource, "App startup should read the multiple instances setting.");
        Assert.Contains("SingleInstanceCoordinator", appSource, "App startup should coordinate secondary launches when multiple instances are disabled.");
        Assert.Contains("RequestActivationOfPrimaryInstance", appSource, "Secondary launches should request activation of the primary instance.");
        return Task.CompletedTask;
    }

    public static Task SettingsNavigationPlacesGeneralAfterLanguage()
    {
        var xamlPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.xaml");
        var enResourcesPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Strings",
            "en-us",
            "Resources.resw");
        var zhResourcesPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Strings",
            "zh-cn",
            "Resources.resw");

        var xaml = File.ReadAllText(xamlPath);
        var enResources = File.ReadAllText(enResourcesPath);
        var zhResources = File.ReadAllText(zhResourcesPath);
        var languageIndex = xaml.IndexOf("x:Name=\"NavLanguage\"", StringComparison.Ordinal);
        var shellIndex = xaml.IndexOf("x:Name=\"NavShell\"", StringComparison.Ordinal);
        var environmentsIndex = xaml.IndexOf("x:Name=\"NavEnvironments\"", StringComparison.Ordinal);
        var sessionsIndex = xaml.IndexOf("x:Name=\"NavSessions\"", StringComparison.Ordinal);
        var devToolsIndex = xaml.IndexOf("x:Name=\"NavDevTools\"", StringComparison.Ordinal);

        Assert.True(languageIndex >= 0, "Settings navigation should include Language.");
        Assert.True(shellIndex >= 0, "Settings navigation should include the General/Shell behavior entry.");
        Assert.True(environmentsIndex >= 0, "Settings navigation should include Environments.");
        Assert.True(sessionsIndex >= 0, "Settings navigation should include Sessions.");
        Assert.True(devToolsIndex >= 0, "Settings navigation should include Dev Tools.");
        Assert.True(languageIndex < shellIndex, "General should appear immediately after Language.");
        Assert.True(shellIndex < environmentsIndex, "Environment management should appear after General.");
        Assert.True(environmentsIndex < sessionsIndex, "Sessions should appear after Environments.");
        Assert.True(sessionsIndex < devToolsIndex, "Dev Tools should stay last.");
        Assert.Contains("<value>General</value>", enResources, "English Shell navigation label should read General.");
        Assert.Contains("SettingsNavShell", zhResources, "Chinese resources should define the General/Shell label.");
        return Task.CompletedTask;
    }
}
