using System.Net;
using System.Reflection;
using OpenClaw.Helpers;
using OpenClaw.Models;
using OpenClaw.Services;

internal static partial class Tests
{
    public static Task TrayWin32UnicodeEntryPointsDeclareUnicodeMarshalling()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "TrayIconService.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("RegisterClassExW\", CharSet = CharSet.Unicode", source, "RegisterClassExW should use Unicode string marshalling.");
        Assert.Contains("CreateWindowExW\", CharSet = CharSet.Unicode", source, "CreateWindowExW should use Unicode string marshalling so the registered class name can be found.");
        Assert.Contains("UnregisterClassW\", CharSet = CharSet.Unicode", source, "UnregisterClassW should use Unicode string marshalling.");
        Assert.Contains("LoadImageW\", CharSet = CharSet.Unicode", source, "LoadImageW should use Unicode string marshalling for the ico path.");
        Assert.Contains("GetModuleHandleW\", CharSet = CharSet.Unicode", source, "GetModuleHandleW should use Unicode string marshalling.");
        Assert.Contains("AppendMenuW\", CharSet = CharSet.Unicode", source, "AppendMenuW should use Unicode string marshalling for menu text.");
        return Task.CompletedTask;
    }

    public static Task TrayCallbackReadsNotifyIconVersion4EventLowWord()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "TrayIconService.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("LowWord(lParam)", source, "NOTIFYICON_VERSION_4 sends the tray event in LOWORD(lParam).");
        Assert.Contains("& 0xFFFF", source, "Tray callback parsing should mask off the high-word icon id.");
        return Task.CompletedTask;
    }

    public static Task TrayContextMenuUsesLocalizedCommandLabels()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "TrayIconService.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("_menuStrings.OpenLabel", source, "Tray menu should use the localized open label.");
        Assert.Contains("_menuStrings.ReloadLabel", source, "Tray menu should use the localized reload label.");
        Assert.Contains("_menuStrings.ViewLogsLabel", source, "Tray menu should use the localized view logs label.");
        Assert.Contains("_menuStrings.SettingsLabel", source, "Tray menu should use the localized settings label.");
        Assert.Contains("_menuStrings.ExitLabel", source, "Tray menu should use the localized exit label.");
        Assert.Contains("MenuStatusHeader", source, "Tray menu should include a status header.");
        Assert.DoesNotContain("\"Hide OpenClaw\"", source, "Minimal tray menu should not expose a hide command.");
        Assert.DoesNotContain("\"Show OpenClaw\"", source, "Minimal tray menu should not expose a show command.");
        Assert.DoesNotContain("\"Open Settings\"", source, "Minimal tray menu should use the shorter settings label.");
        Assert.DoesNotContain("\"Quit\"", source, "Minimal tray menu should use exit terminology.");
        return Task.CompletedTask;
    }

    public static Task TrayContextMenuUsesPopupCapableOwnerWindow()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "TrayIconService.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("WindowHandles.MessageOnly", source, "Tray menu owner should not be a message-only window because popup menus need a normal owner.");
        Assert.DoesNotContain("new(-3)", source, "Tray icon service should not use HWND_MESSAGE for the popup menu owner.");
        return Task.CompletedTask;
    }

    public static Task WindowHideRestoresMinimizedPlacementFirst()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Helpers",
            "WindowFrameHelper.cs");
        var source = File.ReadAllText(sourcePath);
        var hideIndex = source.IndexOf("public static void HideWindow(Window window)", StringComparison.Ordinal);
        var showIndex = source.IndexOf("public static void ShowAndActivateWindow(Window window)", StringComparison.Ordinal);

        Assert.True(hideIndex >= 0, "HideWindow should exist.");
        Assert.True(showIndex > hideIndex, "ShowAndActivateWindow should follow HideWindow.");

        var hideMethod = source[hideIndex..showIndex];
        var minimizedCheckIndex = hideMethod.IndexOf("IsIconic(hwnd)", StringComparison.Ordinal);
        var restoreIndex = hideMethod.IndexOf("ShowWindow(hwnd, ShowWindowRestore)", StringComparison.Ordinal);
        var hideCallIndex = hideMethod.IndexOf("ShowWindow(hwnd, ShowWindowHide)", StringComparison.Ordinal);

        Assert.True(minimizedCheckIndex >= 0, "HideWindow should check whether the HWND is minimized.");
        Assert.True(restoreIndex >= 0, "HideWindow should restore minimized placement before hiding.");
        Assert.True(hideCallIndex > restoreIndex, "HideWindow should hide only after minimized placement is restored.");
        return Task.CompletedTask;
    }

    public static Task AtomicWriterReplacesExistingContent()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, "old");

            AtomicFileWriter.WriteAllText(path, "new");

            Assert.Equal("new", File.ReadAllText(path), "Atomic write should replace the target contents.");
            Assert.Equal(0, Directory.EnumerateFiles(directory, "*.tmp").Count(), "Atomic write should clean temporary files after success.");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static Task LogTailReaderReturnsFinalLines()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "openclaw-2026-05-01.log");
            File.WriteAllLines(path, Enumerable.Range(1, 8).Select(i => $"line-{i}"));

            var tail = LogFileUtilities.ReadLastLines(path, 3);

            Assert.Equal(8, tail.TotalLineCount, "Tail reader should report the full line count.");
            Assert.True(tail.WasTruncated, "Tail reader should indicate when earlier lines were omitted.");
            Assert.Equal("line-6|line-7|line-8", string.Join('|', tail.Lines), "Tail reader should keep only the final requested lines.");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static Task LogRetentionRemovesOnlyExpiredOpenClawLogs()
    {
        var directory = CreateTempDirectory();
        try
        {
            var now = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
            var expired = Path.Combine(directory, "openclaw-2026-04-01.log");
            var recent = Path.Combine(directory, "openclaw-2026-04-30.log");
            var unrelated = Path.Combine(directory, "notes.log");
            File.WriteAllText(expired, "old");
            File.WriteAllText(recent, "new");
            File.WriteAllText(unrelated, "keep");
            File.SetLastWriteTimeUtc(expired, now.AddDays(-30).UtcDateTime);
            File.SetLastWriteTimeUtc(recent, now.AddDays(-1).UtcDateTime);
            File.SetLastWriteTimeUtc(unrelated, now.AddDays(-30).UtcDateTime);

            var deleted = LogFileUtilities.DeleteExpiredLogs(directory, TimeSpan.FromDays(14), now);

            Assert.Equal(1, deleted, "Retention should delete only expired OpenClaw log files.");
            Assert.False(File.Exists(expired), "Expired OpenClaw log should be removed.");
            Assert.True(File.Exists(recent), "Recent OpenClaw log should be preserved.");
            Assert.True(File.Exists(unrelated), "Unrelated files should be preserved.");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static Task TrayMenuStringsAreInjectedAndAccessible()
    {
        var strings = new TrayMenuStrings(
            OpenLabel: "打开 OpenClaw",
            ReloadLabel: "重新加载",
            ViewLogsLabel: "查看日志",
            CompactModeLabel: "紧凑模式",
            SettingsLabel: "设置",
            ExitLabel: "退出");

        Assert.Equal("打开 OpenClaw", strings.OpenLabel, "OpenLabel should be the injected Chinese string.");
        Assert.Equal("重新加载", strings.ReloadLabel, "ReloadLabel should be the injected Chinese string.");
        Assert.Equal("查看日志", strings.ViewLogsLabel, "ViewLogsLabel should be the injected Chinese string.");
        Assert.Equal("紧凑模式", strings.CompactModeLabel, "CompactModeLabel should be the injected Chinese string.");
        Assert.Equal("设置", strings.SettingsLabel, "SettingsLabel should be the injected Chinese string.");
        Assert.Equal("退出", strings.ExitLabel, "ExitLabel should be the injected Chinese string.");
        return Task.CompletedTask;
    }

    public static Task TrayMenuStringsDefaultFallbackUsesEnglish()
    {
        var strings = TrayMenuStrings.Default;

        Assert.Equal("Open OpenClaw", strings.OpenLabel, "Default OpenLabel should be English.");
        Assert.Equal("Reload", strings.ReloadLabel, "Default ReloadLabel should be English.");
        Assert.Equal("View Logs", strings.ViewLogsLabel, "Default ViewLogsLabel should be English.");
        Assert.Equal("Compact Mode", strings.CompactModeLabel, "Default CompactModeLabel should be English.");
        Assert.Equal("Settings", strings.SettingsLabel, "Default SettingsLabel should be English.");
        Assert.Equal("Exit", strings.ExitLabel, "Default ExitLabel should be English.");
        return Task.CompletedTask;
    }

    public static Task TrayMenuExposesReloadAndViewLogsCommands()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "TrayIconService.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("public event Action? ReloadRequested", source, "TrayIconService should expose a ReloadRequested event.");
        Assert.Contains("public event Action? ViewLogsRequested", source, "TrayIconService should expose a ViewLogsRequested event.");
        Assert.Contains("case MenuReload:", source, "TrayIconService should dispatch the reload menu command.");
        Assert.Contains("ReloadRequested?.Invoke()", source, "TrayIconService should raise ReloadRequested when reload is selected.");
        Assert.Contains("case MenuViewLogs:", source, "TrayIconService should dispatch the view logs menu command.");
        Assert.Contains("ViewLogsRequested?.Invoke()", source, "TrayIconService should raise ViewLogsRequested when view logs is selected.");
        return Task.CompletedTask;
    }

    public static Task TrayMenuStatusHeaderReflectsWorkStatus()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "TrayIconService.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("TrayMenuStrings menuStrings", source, "TrayIconService should accept TrayMenuStrings in its constructor.");
        Assert.Contains("private string _statusText", source, "TrayIconService should track the current status text.");
        Assert.Contains("public void UpdateStatus(string statusText)", source, "TrayIconService should expose a status update method.");
        Assert.Contains("$\"Status: {_statusText}\"", source, "TrayIconService should render the current status in the context menu header.");
        return Task.CompletedTask;
    }
}
