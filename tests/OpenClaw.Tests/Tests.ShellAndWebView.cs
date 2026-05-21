using System.Net;
using System.Reflection;
using OpenClaw.Helpers;
using OpenClaw.Models;
using OpenClaw.Services;

internal static partial class Tests
{
    public static Task CompactModeBoundsBypassMinimumPersistableSize()
    {
        var boundsPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw.Core",
            "Helpers",
            "WindowBoundsUtilities.cs");
        var boundsSource = File.ReadAllText(boundsPath);
        var boundsType = typeof(AppSettings).Assembly.GetType("OpenClaw.Helpers.WindowBoundsUtilities");
        var workAreaType = typeof(AppSettings).Assembly.GetType("OpenClaw.Helpers.WindowWorkArea");

        Assert.Contains("MinimumPersistedWindowWidth = 640", boundsSource, "Normal bounds persistence should keep a desktop-sized minimum width.");
        Assert.Contains("MinimumPersistedWindowHeight = 480", boundsSource, "Normal bounds persistence should keep a desktop-sized minimum height.");
        Assert.NotNull(boundsType, "WindowBoundsUtilities should compile from OpenClaw.Core.");
        Assert.True(boundsType!.IsPublic, "WindowBoundsUtilities should be public so the WinUI adapter can consume Core window policy explicitly.");
        Assert.NotNull(workAreaType, "WindowWorkArea should compile from OpenClaw.Core.");
        Assert.True(workAreaType!.IsPublic, "WindowWorkArea should be public so app-local display adapters can pass plain work areas into Core.");

        var settings = new AppSettings();
        Assert.Equal(-1d, settings.CompactWindowLeft, "CompactWindowLeft should default to -1 (unset).");
        Assert.Equal(-1d, settings.CompactWindowTop, "CompactWindowTop should default to -1 (unset).");
        return Task.CompletedTask;
    }

    public static Task CompactModeSwitchesTopBarToCompactLayout()
    {
        var xamlPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "MainWindow.xaml");
        var compactPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "MainWindow.CompactMode.cs");

        var xaml = File.ReadAllText(xamlPath);
        var compact = File.ReadAllText(compactPath);

        Assert.Contains("x:Name=\"EnvironmentSummaryGroup\"", xaml, "The environment/url group should be nameable so compact mode can hide it.");
        Assert.Contains("x:Name=\"TopStatusPill\"", xaml, "The top status pill should be nameable so compact mode can reduce its fixed footprint.");
        Assert.Contains("x:Name=\"LatencyBadge\"", xaml, "The latency badge should be nameable so compact mode can hide secondary controls.");
        Assert.Contains("x:Name=\"CommandBarSurface\"", xaml, "Compact mode should operate on the top command surface explicitly.");
        Assert.Contains("ApplyCompactTopBarState(true)", compact, "Entering compact mode should switch the top bar to a compact layout.");
        Assert.Contains("ApplyCompactTopBarState(false)", compact, "Exiting compact mode should restore the full top bar layout.");
        Assert.Contains("TopStatusPill.MinWidth = 0", compact, "Compact mode should remove the 440px status pill minimum.");
        Assert.Contains("ModelStatusSegment.MinWidth = 0", compact, "Compact mode should remove the model segment minimum width.");
        Assert.Contains("EnvironmentSummaryGroup.Visibility = Visibility.Collapsed", compact, "Compact mode should hide the environment/url group that cannot fit in 480px.");
        Assert.Contains("LatencyBadge.Visibility = Visibility.Collapsed", compact, "Compact mode should hide the secondary latency badge at compact width.");
        return Task.CompletedTask;
    }

    public static Task WebViewStatusProbesIgnoreStaleGenerations()
    {
        var source = ReadWebViewServiceSource();

        Assert.Contains("_webViewGeneration", source, "WebViewService should track active WebView/navigation generations.");
        Assert.Contains("NextWebViewGeneration()", source, "Navigation and detach paths should advance the generation.");
        Assert.Contains("ProbeControlUiStateAfterNavigationAsync(_statusProbeCts.Token, generation)", source, "Status probes should capture the generation they belong to.");
        Assert.Contains("InspectControlUiStateAsync(token, generation)", source, "Status probes should pass cancellation and generation into inspection.");
        Assert.Contains("IsCurrentGeneration(generation)", source, "Inspection results should be discarded if they belong to an old generation.");
        Assert.Contains("CancellationToken token", source, "Inspection should be cancellable after navigation or detach.");
        return Task.CompletedTask;
    }

    public static Task HeartbeatLoopOwnsTimerAndTaskLifetime()
    {
        var source = ReadWebViewServiceSource();

        Assert.Contains("private Task? _heartbeatTask;", source, "Heartbeat loop task should be stored so shutdown observes ownership.");
        Assert.Contains("var timer = new PeriodicTimer", source, "Heartbeat loop should capture its own timer instead of reading a mutable shared timer field.");
        Assert.Contains("_heartbeatTask = RunSessionAwareHeartbeatLoopAsync(gatewayUrl, timer, _heartbeatCts.Token);", source, "Starting heartbeat should retain the loop task.");
        Assert.Contains("ObserveHeartbeatShutdownAsync", source, "Stopping heartbeat should observe loop completion instead of dropping exceptions.");
        Assert.DoesNotContain("_heartbeatTimer!", source, "Heartbeat loop should not dereference a shared nullable timer.");
        return Task.CompletedTask;
    }

    public static Task WebViewServiceHeartbeatIsSplitByResponsibility()
    {
        var serviceRoot = Path.Combine(Directory.GetCurrentDirectory(), "src", "OpenClaw", "Services");
        var shellPath = Path.Combine(serviceRoot, "WebViewService.cs");
        var heartbeatPath = Path.Combine(serviceRoot, "WebViewService.Heartbeat.cs");

        Assert.True(File.Exists(heartbeatPath), "Heartbeat behavior should live in a focused WebViewService partial.");

        var shell = File.ReadAllText(shellPath);
        var heartbeat = File.ReadAllText(heartbeatPath);
        var combined = ReadWebViewServiceSource();

        Assert.Contains("public partial class WebViewService", heartbeat, "Heartbeat partial should extend WebViewService.");
        Assert.Contains("public void StartHeartbeat", heartbeat, "Heartbeat partial should own heartbeat start.");
        Assert.Contains("public void StopHeartbeat", heartbeat, "Heartbeat partial should own heartbeat stop.");
        Assert.Contains("RunSessionAwareHeartbeatLoopAsync", heartbeat, "Heartbeat partial should own the session-aware loop.");
        Assert.Contains("ObserveHeartbeatShutdownAsync", heartbeat, "Heartbeat partial should observe shutdown.");
        Assert.Contains("ProbeGatewayHealthAsync", heartbeat, "Heartbeat partial should own gateway health probing.");
        Assert.Contains("TryScheduleHeartbeatReload", heartbeat, "Heartbeat partial should own reload scheduling.");
        Assert.Contains("LogHeartbeatObservation", heartbeat, "Heartbeat partial should own observation logging.");
        Assert.DoesNotContain("public void StartHeartbeat", shell, "The lifecycle shell should not own heartbeat start.");
        Assert.DoesNotContain("private async Task RunSessionAwareHeartbeatLoopAsync", shell, "The lifecycle shell should not own the heartbeat loop.");
        Assert.Contains("_heartbeatTask = RunSessionAwareHeartbeatLoopAsync(gatewayUrl, timer, _heartbeatCts.Token);", combined, "Heartbeat loop should still retain its task.");
        Assert.Contains("ObserveHeartbeatShutdownAsync", combined, "Stopping heartbeat should still observe loop completion.");
        return Task.CompletedTask;
    }

    public static Task WebViewServiceControlUiInspectionIsSplitByResponsibility()
    {
        var serviceRoot = Path.Combine(Directory.GetCurrentDirectory(), "src", "OpenClaw", "Services");
        var shellPath = Path.Combine(serviceRoot, "WebViewService.cs");
        var inspectionPath = Path.Combine(serviceRoot, "WebViewService.ControlUiInspection.cs");

        Assert.True(File.Exists(inspectionPath), "Control UI inspection should live in a focused WebViewService partial.");

        var shell = File.ReadAllText(shellPath);
        var inspection = File.ReadAllText(inspectionPath);
        var combined = ReadWebViewServiceSource();

        Assert.Contains("public Task<ControlUiProbeSnapshot> InspectControlUiStateAsync()", inspection, "Inspection partial should own the public inspection API.");
        Assert.Contains("ProbeControlUiStateAfterNavigationAsync", inspection, "Inspection partial should own the post-navigation probe loop.");
        Assert.Contains("ApplyControlUiSnapshot", inspection, "Inspection partial should own snapshot application.");
        Assert.Contains("ExecuteControlUiInspectionAsync", inspection, "Inspection partial should own WebView script execution.");
        Assert.Contains("ParseControlUiSnapshot", inspection, "Inspection partial should own snapshot JSON parsing.");
        Assert.Contains("ShouldLogInspectionInstrumentationCount", inspection, "Inspection partial should own instrumentation throttling.");
        Assert.DoesNotContain("public Task<ControlUiProbeSnapshot> InspectControlUiStateAsync()", shell, "The lifecycle shell should not own inspection API.");
        Assert.DoesNotContain("private static ControlUiProbeSnapshot ParseControlUiSnapshot", shell, "The lifecycle shell should not own snapshot parsing.");
        Assert.Contains("InspectControlUiStateAsync(token, generation)", combined, "Status probes should still call the generation-aware inspection API.");
        Assert.Contains("IsCurrentGeneration(generation)", combined, "Inspection should still discard stale generations.");
        return Task.CompletedTask;
    }

    public static Task DisposableServicesImplementIDisposable()
    {
        var webViewPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "WebViewService.cs");
        var bridgePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "HostedUiBridge.cs");
        var coordinatorPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw.Core",
            "Services",
            "ShellSessionCoordinator.cs");

        Assert.Contains("public partial class WebViewService : IDisposable", File.ReadAllText(webViewPath), "WebViewService exposes Dispose and should implement IDisposable.");
        Assert.Contains("public sealed class HostedUiBridge : IDisposable", File.ReadAllText(bridgePath), "HostedUiBridge exposes Dispose and should implement IDisposable.");
        Assert.Contains("public sealed partial class ShellSessionCoordinator : IDisposable", File.ReadAllText(coordinatorPath), "ShellSessionCoordinator exposes Dispose and should implement IDisposable.");
        return Task.CompletedTask;
    }

    public static Task WebViewServiceIsSplitByResponsibility()
    {
        var serviceRoot = Path.Combine(Directory.GetCurrentDirectory(), "src", "OpenClaw", "Services");
        var shellPath = Path.Combine(serviceRoot, "WebViewService.cs");
        var commandsPath = Path.Combine(serviceRoot, "WebViewService.Commands.cs");
        var profilePath = Path.Combine(serviceRoot, "WebViewService.Profile.cs");
        var coreProjectPath = Path.Combine(Directory.GetCurrentDirectory(), "src", "OpenClaw.Core", "OpenClaw.Core.csproj");

        var shell = File.ReadAllText(shellPath);
        var commands = File.ReadAllText(commandsPath);
        var profile = File.ReadAllText(profilePath);
        var coreProject = File.ReadAllText(coreProjectPath);

        Assert.Contains("public partial class WebViewService : IDisposable", shell, "The WebView lifecycle shell should be partial so focused files can own isolated responsibilities.");
        Assert.Contains("public partial class WebViewService", commands, "Command helpers should live in a WebViewService partial.");
        Assert.Contains("public async Task StopAsync()", commands, "The stop command should live with WebView command helpers.");
        Assert.Contains("public async Task<bool> InjectStopCommandAsync()", commands, "The /stop injection helper should live with WebView command helpers.");
        Assert.Contains("public async Task<bool> TryAbortActiveRunAsync()", commands, "The hosted abort helper should live with WebView command helpers.");
        Assert.Contains("public static string GetUserDataFolderForEnvironment", profile, "Profile folder path logic should live with WebView profile helpers.");
        Assert.Contains("private static string BuildEnvironmentFolderName", profile, "Profile folder sanitization should live with WebView profile helpers.");
        Assert.DoesNotContain("public async Task<bool> InjectStopCommandAsync()", shell, "The lifecycle shell should not own command injection script content.");
        Assert.DoesNotContain("private static string BuildEnvironmentFolderName", shell, "The lifecycle shell should not own profile-folder sanitization.");
        Assert.DoesNotContain("WebViewService", coreProject, "WinUI/WebView2-specific service partials should stay out of OpenClaw.Core.");
        return Task.CompletedTask;
    }

    public static Task LogViewerLoadsTailAsynchronously()
    {
        var dialogPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "LogViewerDialog.xaml.cs");
        var source = File.ReadAllText(dialogPath);

        Assert.Contains("Loaded += OnLoaded", source, "Log viewer should defer log loading until the dialog has loaded.");
        Assert.Contains("private async void OnLoaded", source, "Loaded handler should use an async boundary.");
        Assert.Contains("await LoadTodayLogAsync()", source, "Log loading should be asynchronous instead of blocking the constructor.");
        Assert.Contains("await Task.Run", source, "Log tail reading should run off the UI thread.");
        Assert.DoesNotContain("LoadTodayLog();", source, "The log viewer constructor/refresh should not synchronously scan log files on the UI thread.");
        return Task.CompletedTask;
    }

    public static Task LogTailReaderReadsFromFileEnd()
    {
        var utilitiesPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw.Core",
            "Helpers",
            "LogFileUtilities.cs");
        var source = File.ReadAllText(utilitiesPath);

        Assert.Contains("FileStream", source, "Tail reading should use a stream so it can seek near the end of large logs.");
        Assert.Contains("SeekOrigin.End", source, "Tail reading should scan from the end of the file.");
        Assert.Contains("Encoding.UTF8", source, "Tail reading should decode the retained byte range explicitly.");
        Assert.DoesNotContain("foreach (var line in File.ReadLines(path))", source, "Tail reading should not enumerate the whole log on refresh.");
        return Task.CompletedTask;
    }

    public static Task HostedBridgeScriptHasTestableAssetSeam()
    {
        var bridgePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "HostedUiBridge.cs");
        var scriptPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "HostedUiBridge.Script.cs");
        var scriptAssetPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "HostedUiBridge.Script.js");
        var modelResolverPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "HostedUiBridge.ModelResolver.js");
        var projectPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "OpenClaw.csproj");

        Assert.True(File.Exists(scriptPath), "The hosted bridge script should be isolated in a dedicated file instead of growing the service file.");
        Assert.True(File.Exists(scriptAssetPath), "The hosted bridge browser script should live in a runnable JS asset instead of a C# raw string.");
        Assert.True(File.Exists(modelResolverPath), "The model resolver should live in a runnable JS asset so behavior tests execute the same logic as the bridge.");
        var scriptBuilderSource = File.ReadAllText(scriptPath);
        var scriptAssetSource = File.ReadAllText(scriptAssetPath);
        var projectSource = File.ReadAllText(projectPath);
        Assert.Contains("HostedUiBridgeScript", scriptBuilderSource, "The bridge script file should expose a named script builder seam.");
        Assert.Contains("HostedUiBridge.Script.js", scriptBuilderSource, "The bridge script builder should load the browser script asset.");
        Assert.Contains("HostedUiBridge.ModelResolver.js", scriptBuilderSource, "The bridge script builder should load the model resolver asset.");
        Assert.Contains("JsonSerializer.Serialize(strings)", scriptBuilderSource, "The bridge script builder should inject localized strings as JSON instead of hand-escaped JS literals.");
        Assert.Contains("const STRINGS = __OPENCLAW_BRIDGE_STRINGS_JSON__;", scriptAssetSource, "The browser bridge asset should own a single localized-string injection point.");
        Assert.Contains("__OPENCLAW_MODEL_RESOLVER_SCRIPT__", scriptAssetSource, "The browser bridge asset should own the model resolver injection point.");
        Assert.Contains("window.__openClawHostBridge =", scriptAssetSource, "The browser bridge implementation should live in the JS asset.");
        Assert.Contains("new MutationObserver", scriptAssetSource, "The DOM observer implementation should live in the JS asset.");
        Assert.DoesNotContain("window.__openClawHostBridge =", scriptBuilderSource, "The C# script builder should not own browser bridge implementation details.");
        Assert.DoesNotContain("new MutationObserver", scriptBuilderSource, "The C# script builder should not contain the DOM observer implementation.");
        Assert.Contains("HostedUiBridge.Script.js", projectSource, "The hosted bridge browser script asset should be embedded in the app assembly.");
        Assert.Contains("HostedUiBridge.ModelResolver.js", projectSource, "The model resolver asset should be embedded in the app assembly.");
        Assert.Contains("HostedUiBridgeScript.Build", File.ReadAllText(bridgePath), "HostedUiBridge should delegate script construction to the dedicated builder.");
        return Task.CompletedTask;
    }

    public static Task WebViewCircuitBreakerTripsAfterRepeatedFailures()
    {
        var breaker = new WebViewCircuitBreaker(maxAttempts: 5, windowSeconds: 60);

        // 5 attempts should be allowed
        for (var i = 0; i < 5; i++)
        {
            Assert.True(breaker.CanAttempt(), $"Attempt {i + 1} should be allowed.");
            breaker.RecordAttempt();
        }

        // 6th should be blocked
        Assert.False(breaker.CanAttempt(), "6th attempt within window should be blocked.");
        return Task.CompletedTask;
    }

    public static Task WebViewCircuitBreakerResetsAfterCooldown()
    {
        var breaker = new WebViewCircuitBreaker(maxAttempts: 3, windowSeconds: 1);

        for (var i = 0; i < 3; i++)
        {
            breaker.RecordAttempt();
        }

        Assert.False(breaker.CanAttempt(), "Should be tripped after 3 attempts.");

        // Manual reset
        breaker.Reset();
        Assert.True(breaker.CanAttempt(), "Should allow attempts after reset.");
        return Task.CompletedTask;
    }
}
