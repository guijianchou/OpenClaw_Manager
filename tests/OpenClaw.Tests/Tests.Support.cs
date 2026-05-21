using System.Net;
using System.Reflection;
using OpenClaw.Helpers;
using OpenClaw.Models;
using OpenClaw.Services;

internal static partial class Tests
{
    private static ControlUiProbeSnapshot CreateAuthRequiredSnapshot()
    {
        return new ControlUiProbeSnapshot(
            ControlUiPhase.AuthRequired,
            "Auth required",
            "Sign in again",
            "https://gateway.example/login",
            ShellDetected: false,
            IsBusy: false,
            InputFocused: false,
            WorkState: "idle",
            CurrentModel: string.Empty);
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "OpenClaw.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("Timed out waiting for the expected condition.");
    }

    private static Task? GetCurrentProbeTask(ControlUiLatencyService service)
    {
        var field = typeof(ControlUiLatencyService).GetField("_probeTask", BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(service) as Task;
    }

    private static string ExtractTopStatusPillXaml(string xaml)
    {
        const string startMarker = "x:Name=\"TopStatusPill\"";
        const string endMarker = "</Border>";
        var start = xaml.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, "TopStatusPill should be present in MainWindow.xaml.");

        var end = xaml.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end >= 0, "TopStatusPill closing Border should be present in MainWindow.xaml.");

        return xaml.Substring(start, end + endMarker.Length - start);
    }

    private static string ReadAppResourceXaml()
    {
        var appRoot = Path.Combine(Directory.GetCurrentDirectory(), "src", "OpenClaw");
        var resourcePaths = new[]
        {
            Path.Combine(appRoot, "App.xaml"),
            Path.Combine(appRoot, "Styles", "Colors.xaml"),
            Path.Combine(appRoot, "Styles", "StatusResources.xaml"),
            Path.Combine(appRoot, "Styles", "ButtonStyles.xaml")
        };

        return string.Join(Environment.NewLine, resourcePaths.Select(File.ReadAllText));
    }

    private static string ReadWebViewServiceSource()
    {
        var serviceRoot = Path.Combine(Directory.GetCurrentDirectory(), "src", "OpenClaw", "Services");
        var servicePaths = Directory.GetFiles(serviceRoot, "WebViewService*.cs")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        return string.Join(Environment.NewLine, servicePaths.Select(File.ReadAllText));
    }

    private static string ExtractStyleXaml(string xaml, string styleKey)
    {
        var startMarker = $"x:Key=\"{styleKey}\"";
        const string endMarker = "</Style>";
        var start = xaml.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{styleKey} should be present in app resources.");

        var end = xaml.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end >= 0, $"{styleKey} closing Style should be present in app resources.");

        return xaml.Substring(start, end + endMarker.Length - start);
    }

    private static ShellSessionCoordinator CreateCoordinator(
        FakeShellSessionWebView webView,
        FakeShellSessionBridge bridge,
        RecoveryPolicyOptions? recoveryOptions = null,
        HeartbeatOptions? heartbeatOptions = null)
    {
        var coordinator = new ShellSessionCoordinator();
        coordinator.AttachAsync(
            webView,
            bridge,
            recoveryOptions ?? new RecoveryPolicyOptions
            {
                ReconnectDelayMs = 1,
                MaxReconnectDelayMs = 1,
                ReconnectBackoffMultiplier = 1,
                HardRefreshCooldownSeconds = 0
            },
            heartbeatOptions ?? new HeartbeatOptions()).GetAwaiter().GetResult();
        return coordinator;
    }
}

internal sealed class TestLogger : IAppLogger
{
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message) { }
    public void Info(string eventKey, object? context = null) { }
    public void Warning(string eventKey, object? context = null) { }
    public void Error(string eventKey, object? context = null) { }
}

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
    {
        _sendAsync = sendAsync;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        _sendAsync(request, cancellationToken);
}

internal sealed class FakeShellSessionWebView : IShellSessionWebView
{
    private readonly Queue<ControlUiProbeSnapshot> _snapshots = new();

    public int InspectCount { get; private set; }

    public int ReloadCount { get; private set; }

    public event Action<ConnectionState>? ConnectionStateChanged
    {
        add { }
        remove { }
    }

    public event Action<string>? NavigationErrorOccurred
    {
        add { }
        remove { }
    }

    public event Action<string?>? NavigationCompleted
    {
        add { }
        remove { }
    }

    public event Action<HeartbeatProbeResult>? HeartbeatObserved
    {
        add { }
        remove { }
    }

    public event Action<string>? HeartbeatFailed
    {
        add { }
        remove { }
    }

    public event Action<ControlUiProbeSnapshot>? ControlUiSnapshotUpdated;

    public void EnqueueSnapshot(ControlUiProbeSnapshot snapshot)
    {
        _snapshots.Enqueue(snapshot);
    }

    public void RaiseControlUiSnapshotUpdated(ControlUiProbeSnapshot snapshot)
    {
        ControlUiSnapshotUpdated?.Invoke(snapshot);
    }

    public Task<ControlUiProbeSnapshot> InspectControlUiStateAsync()
    {
        InspectCount++;

        if (_snapshots.Count == 0)
        {
            return Task.FromResult(ControlUiProbeSnapshot.Unknown);
        }

        return Task.FromResult(_snapshots.Dequeue());
    }

    public void Reload()
    {
        ReloadCount++;
    }

    public int TotalControlUiInspectionRequests => InspectCount;

    public int CachedControlUiInspectionRequests => 0;

    public int CoalescedControlUiInspectionRequests => 0;

    public int HeartbeatRecoveryRequests => 0;
}

internal sealed class FakeShellSessionBridge : IShellSessionBridge
{
    public bool SessionRefreshResult { get; set; }
    public bool RecentMessagesResult { get; set; }
    public bool LightweightSyncResult { get; set; }
    public bool ReconnectIntentResult { get; set; }

    public int RequestSessionRefreshCalls { get; private set; }
    public int RequestRecentMessagesCalls { get; private set; }
    public int RequestLightweightSyncCalls { get; private set; }
    public int NotifyReconnectIntentCalls { get; private set; }

    public event Action<SessionReadyEventArgs>? SessionReady
    {
        add { }
        remove { }
    }
    public event Action<EventGapEventArgs>? EventGapDetected;

    public Task<bool> RequestSessionRefreshAsync()
    {
        RequestSessionRefreshCalls++;
        return Task.FromResult(SessionRefreshResult);
    }

    public Task<bool> RequestRecentMessagesAsync()
    {
        RequestRecentMessagesCalls++;
        return Task.FromResult(RecentMessagesResult);
    }

    public Task<bool> RequestLightweightSyncAsync()
    {
        RequestLightweightSyncCalls++;
        return Task.FromResult(LightweightSyncResult);
    }

    public Task<bool> NotifyReconnectIntentAsync()
    {
        NotifyReconnectIntentCalls++;
        return Task.FromResult(ReconnectIntentResult);
    }

    public void RaiseEventGap(EventGapEventArgs args)
    {
        EventGapDetected?.Invoke(args);
    }
}

internal static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void False(bool condition, string message)
    {
        if (condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}; Actual: {actual}");
        }
    }

    public static void Null(object? value, string message)
    {
        if (value is not null)
        {
            throw new InvalidOperationException($"{message} Value was: {value}");
        }
    }

    public static void NotNull(object? value, string message)
    {
        if (value is null)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void Contains(string expectedSubstring, string actual, string message)
    {
        if (!actual.Contains(expectedSubstring, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{message} Expected substring: {expectedSubstring}; Actual: {actual}");
        }
    }

    public static void DoesNotContain(string unexpectedSubstring, string actual, string message)
    {
        if (actual.Contains(unexpectedSubstring, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{message} Unexpected substring: {unexpectedSubstring}");
        }
    }
}
