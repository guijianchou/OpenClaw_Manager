// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;

namespace OpenClaw.Services;

/// <summary>
/// Collects diagnostic data (logs, redacted settings, runtime info) and exports as a zip bundle.
/// </summary>
public static partial class DiagnosticBundleService
{
    private static readonly TimeSpan DefaultLogRetention = TimeSpan.FromDays(7);

    /// <summary>
    /// Redacts sensitive fields from a settings JSON string.
    /// - Gateway URLs: scheme preserved, host replaced with &lt;host&gt;, path replaced with &lt;path&gt;.
    /// - Token/key/secret field values: replaced with &lt;redacted&gt;.
    /// </summary>
    public static string RedactSettingsJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        // Redact URLs: https://anything.com/path -> https://<host>/<path>
        var redacted = UrlPattern().Replace(json, match =>
        {
            var scheme = match.Groups[1].Value;
            return $"{scheme}://<host>/<path>";
        });

        // Redact token/key/secret values
        redacted = TokenPattern().Replace(redacted, match =>
        {
            var prefix = match.Groups[1].Value;
            return $"{prefix}\"<redacted>\"";
        });

        return redacted;
    }

    /// <summary>
    /// Collects runtime environment information as a human-readable string.
    /// </summary>
    public static string CollectRuntimeInfo()
    {
        var lines = new List<string>
        {
            $"OS: {Environment.OSVersion}",
            $".NET: {Environment.Version}",
            $"App: {GetAppVersion()}",
            $"Architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}",
            $"Machine: {Environment.MachineName}",
            $"Processors: {Environment.ProcessorCount}",
        };

        // Try to get WebView2 version via reflection (avoids hard dependency on WebView2 in Core)
        try
        {
            var envType = Type.GetType("Microsoft.Web.WebView2.Core.CoreWebView2Environment, Microsoft.Web.WebView2.Core");
            var method = envType?.GetMethod("GetAvailableBrowserVersionString", Type.EmptyTypes);
            var version = method?.Invoke(null, null) as string;
            lines.Add($"WebView2: {version ?? "unknown"}");
        }
        catch
        {
            lines.Add("WebView2: unavailable");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Collects log files from the specified directory that are within the retention window.
    /// </summary>
    public static IReadOnlyList<string> CollectRecentLogFiles(string logsDirectory, TimeSpan? retention = null)
    {
        var maxAge = retention ?? DefaultLogRetention;
        var cutoff = DateTimeOffset.UtcNow - maxAge;

        if (!Directory.Exists(logsDirectory))
        {
            return [];
        }

        return Directory.GetFiles(logsDirectory, "openclaw-*.log")
            .Where(f => File.GetLastWriteTimeUtc(f) >= cutoff.UtcDateTime)
            .OrderByDescending(f => f)
            .ToArray();
    }

    /// <summary>
    /// Creates a diagnostic zip bundle at the specified output path.
    /// </summary>
    public static async Task<string> ExportBundleAsync(
        string settingsJson,
        string logsDirectory,
        string diagnosticSummary,
        string outputDirectory)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmm");
        var fileName = $"openclaw-diagnostics-{timestamp}.zip";
        var outputPath = Path.Combine(outputDirectory, fileName);

        Directory.CreateDirectory(outputDirectory);

        using var zipStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);

        // 1. Redacted settings
        var redactedSettings = RedactSettingsJson(settingsJson);
        await AddTextEntryAsync(archive, "settings-redacted.json", redactedSettings);

        // 2. Runtime info
        var runtimeInfo = CollectRuntimeInfo();
        await AddTextEntryAsync(archive, "runtime-info.txt", runtimeInfo);

        // 3. Diagnostic summary
        if (!string.IsNullOrWhiteSpace(diagnosticSummary))
        {
            await AddTextEntryAsync(archive, "diagnostic-summary.txt", diagnosticSummary);
        }

        // 4. Recent log files
        var logFiles = CollectRecentLogFiles(logsDirectory);
        foreach (var logFile in logFiles)
        {
            var entryName = $"logs/{Path.GetFileName(logFile)}";
            archive.CreateEntryFromFile(logFile, entryName, CompressionLevel.Optimal);
        }

        return outputPath;
    }

    private static async Task AddTextEntryAsync(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var writer = new StreamWriter(entry.Open());
        await writer.WriteAsync(content);
    }

    [GeneratedRegex(@"(https?|wss?)://[^""'\s,}\]]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"(""(?:[^""]*(?:token|key|secret|password|credential)[^""]*)""\s*:\s*)""[^""]+""", RegexOptions.IgnoreCase)]
    private static partial Regex TokenPattern();

    private static string GetAppVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version is not null
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "unknown";
    }
}
