using System.Net;
using System.Reflection;
using OpenClaw.Helpers;
using OpenClaw.Models;
using OpenClaw.Services;

internal static partial class Tests
{
    public static Task HotkeyBindingParsesStandardModifierKeyString()
    {
        var binding = HotkeyBinding.Parse("Ctrl+Alt+Space");
        Assert.NotNull(binding, "Parse should succeed for a valid hotkey string.");
        Assert.True(binding!.Ctrl, "Ctrl modifier should be set.");
        Assert.True(binding.Alt, "Alt modifier should be set.");
        Assert.False(binding.Shift, "Shift modifier should not be set.");
        Assert.False(binding.Win, "Win modifier should not be set.");
        Assert.Equal("Space", binding.Key, "Key should be 'Space'.");
        return Task.CompletedTask;
    }

    public static Task HotkeyBindingRoundTripsThroughToString()
    {
        var original = "Ctrl+Shift+F12";
        var binding = HotkeyBinding.Parse(original);
        Assert.NotNull(binding, "Parse should succeed.");
        var serialized = binding!.ToString();
        var reparsed = HotkeyBinding.Parse(serialized);
        Assert.NotNull(reparsed, "Re-parse should succeed.");
        Assert.True(reparsed!.Ctrl, "Ctrl should survive round-trip.");
        Assert.True(reparsed.Shift, "Shift should survive round-trip.");
        Assert.False(reparsed.Alt, "Alt should not be set after round-trip.");
        Assert.Equal("F12", reparsed.Key, "Key should survive round-trip.");
        return Task.CompletedTask;
    }

    public static Task HotkeyBindingParseReturnsNullForInvalidInput()
    {
        Assert.Null(HotkeyBinding.Parse(null), "Null input should return null.");
        Assert.Null(HotkeyBinding.Parse(""), "Empty input should return null.");
        Assert.Null(HotkeyBinding.Parse("   "), "Whitespace input should return null.");
        Assert.Null(HotkeyBinding.Parse("+"), "Lone plus should return null.");
        Assert.Null(HotkeyBinding.Parse("Ctrl+"), "Modifier without key should return null.");
        return Task.CompletedTask;
    }

    public static Task HotkeyBindingParseSingleKeyWithoutModifier()
    {
        var binding = HotkeyBinding.Parse("F5");
        Assert.NotNull(binding, "Single key without modifier should parse.");
        Assert.False(binding!.Ctrl, "No Ctrl.");
        Assert.False(binding.Alt, "No Alt.");
        Assert.False(binding.Shift, "No Shift.");
        Assert.False(binding.Win, "No Win.");
        Assert.Equal("F5", binding.Key, "Key should be F5.");
        return Task.CompletedTask;
    }

    public static Task AppSettingsDefaultsHotkeyToCtrlAltSpaceEnabled()
    {
        var settings = new AppSettings();
        Assert.Equal("Ctrl+Alt+Space", settings.GlobalHotkey, "Default hotkey should be Ctrl+Alt+Space.");
        Assert.True(settings.EnableGlobalHotkey, "Global hotkey should be enabled by default.");
        return Task.CompletedTask;
    }

    public static Task SettingsLoadWithoutHotkeyFieldsUsesDefaults()
    {
        var directory = CreateTempDirectory();
        try
        {
            // Write a minimal settings.json without hotkey fields
            var settingsPath = Path.Combine(directory, "settings.json");
            File.WriteAllText(settingsPath, """{"appTheme":"Dark","environments":[]}""");

            var service = new ConfigurationService(directory, new TestLogger());
            service.Load();

            Assert.Equal("Ctrl+Alt+Space", service.Settings.GlobalHotkey, "Missing hotkey field should default to Ctrl+Alt+Space.");
            Assert.True(service.Settings.EnableGlobalHotkey, "Missing enable field should default to true.");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static Task SettingsShellExposesGlobalHotkeyControls()
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
        var actions = File.ReadAllText(actionsPath);
        var enResources = File.ReadAllText(enResourcesPath);
        var zhResources = File.ReadAllText(zhResourcesPath);

        Assert.Contains("SettingsEnableGlobalHotkey", xaml, "Shell settings should expose a global hotkey enable settings row.");
        Assert.Contains("IsOn=\"{x:Bind ViewModel.EnableGlobalHotkey, Mode=TwoWay}\"", xaml, "Global hotkey switch should bind to SettingsViewModel.");
        Assert.Contains("SettingsGlobalHotkey", xaml, "Shell settings should expose a hotkey input label.");
        Assert.Contains("Text=\"{x:Bind ViewModel.GlobalHotkey, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", xaml, "Global hotkey input should bind to SettingsViewModel.");
        Assert.Contains("OnResetHotkeyClick", xaml, "Shell settings should include a reset-to-default hotkey button.");
        Assert.Contains("ResetGlobalHotkey()", actions, "Reset button should reset through SettingsViewModel.");
        Assert.Contains("<value>Global hotkey</value>", enResources, "English hotkey label should be present.");
        Assert.Contains("<value>全局热键</value>", zhResources, "Chinese hotkey label should be present.");
        return Task.CompletedTask;
    }

    public static Task SettingsViewModelPersistsAndValidatesGlobalHotkeyFields()
    {
        var viewModelPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "ViewModels",
            "SettingsViewModel.cs");
        var source = File.ReadAllText(viewModelPath);

        Assert.Contains("public bool EnableGlobalHotkey", source, "SettingsViewModel should expose the global hotkey enabled flag.");
        Assert.Contains("public string GlobalHotkey", source, "SettingsViewModel should expose the global hotkey binding string.");
        Assert.Contains("Settings.EnableGlobalHotkey = EnableGlobalHotkey", source, "Settings save should persist the hotkey enabled flag.");
        Assert.Contains("Settings.GlobalHotkey = GlobalHotkey", source, "Settings save should persist the hotkey binding.");
        Assert.Contains("ResetGlobalHotkey()", source, "SettingsViewModel should expose a reset method for the default hotkey.");
        Assert.Contains("TryValidateHotkey", source, "Settings save should validate the hotkey before persisting.");
        Assert.Contains("HotkeyBinding.Parse(GlobalHotkey)", source, "Hotkey validation should use the shared parser.");
        Assert.Contains("binding.GetVirtualKeyCode() == 0", source, "Hotkey validation should reject keys that cannot be registered.");
        return Task.CompletedTask;
    }

    public static Task DiagnosticInstrumentationIncludesHostedUiStreamState()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "DiagnosticService.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("LatestControlUiSnapshot", source, "Diagnostic instrumentation should include the current hosted UI snapshot.");
        Assert.Contains("IsBusyStale", source, "Diagnostic instrumentation should report stale busy state.");
        Assert.Contains("BusyStaleSeconds", source, "Diagnostic instrumentation should report stale busy duration.");
        Assert.Contains("FocusedInputHasText", source, "Diagnostic instrumentation should distinguish focused empty input from unsent user text.");
        return Task.CompletedTask;
    }

    public static Task DiagnosticBundleRedactsGatewayUrlHost()
    {
        var input = """{"environments":[{"name":"prod","gatewayUrl":"https://my-secret-host.example.com/control"}]}""";
        var redacted = DiagnosticBundleService.RedactSettingsJson(input);

        Assert.DoesNotContain("my-secret-host.example.com", redacted, "Host should be redacted from settings JSON.");
        Assert.Contains("<host>", redacted, "Redacted host placeholder should be present.");
        Assert.DoesNotContain("/control", redacted, "Path should be redacted from settings JSON.");
        return Task.CompletedTask;
    }

    public static Task DiagnosticBundleRedactsTokenLikeValues()
    {
        var input = """{"environments":[{"name":"dev","gatewayUrl":"https://x.com"}],"someToken":"abc123secret","apiKey":"sk-live-xyz"}""";
        var redacted = DiagnosticBundleService.RedactSettingsJson(input);

        Assert.DoesNotContain("abc123secret", redacted, "Token value should be redacted.");
        Assert.DoesNotContain("sk-live-xyz", redacted, "API key value should be redacted.");
        Assert.Contains("<redacted>", redacted, "Redacted placeholder should be present for token-like fields.");
        return Task.CompletedTask;
    }

    public static Task DiagnosticBundleIncludesRuntimeInfo()
    {
        var info = DiagnosticBundleService.CollectRuntimeInfo();

        Assert.Contains("OS:", info, "Runtime info should include OS.");
        Assert.Contains(".NET:", info, "Runtime info should include .NET version.");
        Assert.Contains("App:", info, "Runtime info should include app version.");
        return Task.CompletedTask;
    }

    public static Task DiagnosticBundleCollectsRecentLogFiles()
    {
        var directory = CreateTempDirectory();
        try
        {
            var logsDir = Path.Combine(directory, "logs");
            Directory.CreateDirectory(logsDir);

            var today = DateTimeOffset.UtcNow;
            // Create logs: 2 recent, 1 old (>7 days)
            var todayLog = Path.Combine(logsDir, $"openclaw-{today:yyyy-MM-dd}.log");
            var recentLog = Path.Combine(logsDir, $"openclaw-{today.AddDays(-3):yyyy-MM-dd}.log");
            var oldLog = Path.Combine(logsDir, $"openclaw-{today.AddDays(-10):yyyy-MM-dd}.log");
            File.WriteAllText(todayLog, "today's log");
            File.WriteAllText(recentLog, "3 days ago");
            File.WriteAllText(oldLog, "10 days ago");
            File.SetLastWriteTimeUtc(todayLog, today.UtcDateTime);
            File.SetLastWriteTimeUtc(recentLog, today.AddDays(-3).UtcDateTime);
            File.SetLastWriteTimeUtc(oldLog, today.AddDays(-10).UtcDateTime);

            var files = DiagnosticBundleService.CollectRecentLogFiles(logsDir, TimeSpan.FromDays(7));

            Assert.Equal(2, files.Count, "Should collect only logs within 7 days.");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static Task CloudflareRayParsesPopFromStandardHeader()
    {
        // Standard cf-ray format: hex-POP
        Assert.Equal("LAX", CloudflareRayParser.ParsePoP("8a1b2c3d4e5f6789-LAX"), "Should extract 3-letter PoP code.");
        Assert.Equal("HKG", CloudflareRayParser.ParsePoP("abcdef1234567890-HKG"), "Should extract HKG.");
        Assert.Equal("NRT", CloudflareRayParser.ParsePoP("0000000000000000-NRT"), "Should extract NRT.");
        // Lowercase should also work
        Assert.Equal("SJC", CloudflareRayParser.ParsePoP("aabbccdd11223344-sjc"), "Should handle lowercase PoP and uppercase it.");
        return Task.CompletedTask;
    }

    public static Task CloudflareRayReturnsNullForMissingOrMalformed()
    {
        Assert.Null(CloudflareRayParser.ParsePoP(null), "Null input should return null.");
        Assert.Null(CloudflareRayParser.ParsePoP(""), "Empty input should return null.");
        Assert.Null(CloudflareRayParser.ParsePoP("8a1b2c3d4e5f6789"), "No dash means no PoP.");
        Assert.Null(CloudflareRayParser.ParsePoP("8a1b2c3d4e5f6789-"), "Trailing dash with no code should return null.");
        Assert.Null(CloudflareRayParser.ParsePoP("8a1b2c3d4e5f6789-AB"), "Two-letter code is not a valid PoP.");
        return Task.CompletedTask;
    }

    public static Task LatencyTooltipIncludesPopWhenAvailable()
    {
        var summary = new LatencyHistorySummary(5, 42, 30, 38, 45, 50);
        var tooltip = LatencyTooltipFormatter.Format(summary, "LAX");

        Assert.Contains("PoP: LAX", tooltip, "Tooltip should include PoP line when available.");
        Assert.Contains("Latest: 42 ms", tooltip, "Tooltip should still include latency data.");
        return Task.CompletedTask;
    }

    public static async Task SingleInstanceStopAwaitsListenerTaskCompletion()
    {
        // Verify that SingleInstanceCoordinator exposes a StopAsync method
        var type = typeof(SingleInstanceCoordinator);
        var stopMethod = type.GetMethod("StopAsync");
        Assert.NotNull(stopMethod, "SingleInstanceCoordinator should expose a StopAsync method.");

        // Verify StopAsync returns Task
        Assert.Equal(typeof(Task), stopMethod!.ReturnType, "StopAsync should return Task.");

        // Functional test: create a primary coordinator, start listening, then stop
        var coordinator = SingleInstanceCoordinator.CreatePrimaryOrSecondary(new TestLogger());
        if (coordinator.IsPrimary)
        {
            coordinator.StartListening();
            await coordinator.StopAsync();
            // After StopAsync, the coordinator should be safe to dispose without race
            coordinator.Dispose();
        }
        else
        {
            // If not primary (unlikely in test), just dispose
            coordinator.Dispose();
        }
    }

    public static Task AppSettingsDefaultsAlwaysOnTopToFalse()
    {
        var settings = new AppSettings();
        Assert.False(settings.AlwaysOnTop, "AlwaysOnTop should default to false.");
        return Task.CompletedTask;
    }

    public static Task AlwaysOnTopAppliesNativeTopmostFallback()
    {
        var alwaysOnTopPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "MainWindow.AlwaysOnTop.cs");
        var frameHelperPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Helpers",
            "WindowFrameHelper.cs");

        var alwaysOnTop = File.ReadAllText(alwaysOnTopPath);
        var frameHelper = File.ReadAllText(frameHelperPath);

        Assert.Contains("WindowFrameHelper.SetTopMost(this, value)", alwaysOnTop, "Always-on-top should use a native topmost fallback, not only AppWindow.Presenter.");
        Assert.Contains("public static bool SetTopMost(Window window, bool value)", frameHelper, "WindowFrameHelper should expose a native SetWindowPos topmost helper.");
        Assert.Contains("new(-1)", frameHelper, "Native topmost helper should use HWND_TOPMOST.");
        Assert.Contains("new(-2)", frameHelper, "Native topmost helper should use HWND_NOTOPMOST.");
        return Task.CompletedTask;
    }

    public static Task AlwaysOnTopPinButtonUsesAccentColorWhenActive()
    {
        var alwaysOnTopPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "MainWindow.AlwaysOnTop.cs");

        var alwaysOnTop = File.ReadAllText(alwaysOnTopPath);

        Assert.Contains("PinButton.Foreground", alwaysOnTop, "Pinned state should be visible through the Pin button foreground.");
        Assert.Contains("AccentTextFillColorPrimaryBrush", alwaysOnTop, "Pinned state should use a theme-aware accent text brush.");
        Assert.Contains("TextFillColorSecondaryBrush", alwaysOnTop, "Unpinned state should use a theme-aware secondary text brush so it remains visible on light backgrounds.");
        return Task.CompletedTask;
    }

    public static Task SettingsSaveReappliesLiveShellOptions()
    {
        var sharedPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.Shared.cs");
        var viewModelPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "ViewModels",
            "SettingsViewModel.cs");
        var commandsPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "MainWindow.Commands.cs");
        var hotkeyPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "MainWindow.Hotkey.cs");

        var shared = File.ReadAllText(sharedPath);
        var viewModel = File.ReadAllText(viewModelPath);
        var commands = File.ReadAllText(commandsPath);
        var hotkey = File.ReadAllText(hotkeyPath);

        Assert.Contains("DidChangeLiveShellOptions", shared, "Settings save results should report live shell option changes.");
        Assert.Contains("_originalAlwaysOnTop", viewModel, "Settings view model should compare the saved always-on-top value against the original value.");
        Assert.Contains("_originalEnableGlobalHotkey", viewModel, "Settings view model should compare the saved hotkey enable flag against the original value.");
        Assert.Contains("_originalGlobalHotkey", viewModel, "Settings view model should compare the saved hotkey binding against the original value.");
        Assert.Contains("ApplyLiveShellSettings()", commands, "MainWindow should reapply live shell settings after Settings is saved.");
        Assert.Contains("SetAlwaysOnTop(App.Configuration.Settings.AlwaysOnTop)", commands, "Saving Settings should update the current topmost state immediately.");
        Assert.Contains("ReapplyGlobalHotkey()", commands, "Saving Settings should update the registered hotkey immediately.");
        Assert.Contains("private void ReapplyGlobalHotkey()", hotkey, "Hotkey registration should expose a safe reapply path.");
        Assert.Contains("DisposeGlobalHotkey();", hotkey, "Hotkey reapply should unregister the old binding before registering the new one.");
        return Task.CompletedTask;
    }

    public static Task AppSettingsDefaultsCompactModeToFalse()
    {
        var settings = new AppSettings();
        Assert.False(settings.CompactMode, "CompactMode should default to false.");
        return Task.CompletedTask;
    }
}
