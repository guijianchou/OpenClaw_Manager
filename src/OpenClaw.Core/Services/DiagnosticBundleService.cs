// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenClaw.Services;

public sealed record DiagnosticRuntimeInfo(
    string? WebView2RuntimeVersion,
    string OsVersion,
    string DotNetVersion,
    string AppVersion,
    string ProcessArchitecture,
    int ProcessorCount,
    string MachineHash);

/// <summary>
/// Collects diagnostic data (logs, redacted settings, runtime info) and exports as a zip bundle.
/// </summary>
public static partial class DiagnosticBundleService
{
    private static readonly TimeSpan DefaultLogRetention = TimeSpan.FromDays(7);
    private const long MaxBundledLogFileBytes = 5 * 1024 * 1024;
    private const long MaxBundledLogPayloadBytes = 8 * 1024 * 1024;
    private const int MaxBundledLogFileCount = 20;
    private const int MaxDiagnosticTextEntryBytes = 1024 * 1024;

    /// <summary>
    /// Redacts sensitive fields from a settings JSON string.
    /// - Gateway URLs: scheme preserved, host replaced with &lt;host&gt;, path replaced with &lt;path&gt;.
    /// - Token/key/secret field values: replaced with &lt;redacted&gt;.
    /// </summary>
    public static string RedactSettingsJson(string json)
    {
        return RedactDiagnosticText(json);
    }

    public static string RedactDiagnosticText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var redacted = UrlPattern().Replace(text, match =>
        {
            var scheme = match.Groups[1].Value;
            return $"{scheme}://<host>/<path>";
        });

        redacted = TokenPattern().Replace(redacted, match =>
        {
            var prefix = match.Groups[1].Value;
            return $"{prefix}\"<redacted>\"";
        });

        redacted = KeyValueSecretPattern().Replace(redacted, match =>
        {
            var prefix = match.Groups[1].Value;
            return $"{prefix}<redacted>";
        });

        redacted = HeaderSecretPattern().Replace(redacted, match =>
        {
            var prefix = match.Groups[1].Value;
            return $"{prefix}<redacted>";
        });

        return redacted;
    }

    /// <summary>
    /// Collects runtime environment information from platform-neutral inputs.
    /// </summary>
    public static DiagnosticRuntimeInfo CollectRuntimeInfo(string? webView2RuntimeVersion)
    {
        return new DiagnosticRuntimeInfo(
            WebView2RuntimeVersion: webView2RuntimeVersion,
            OsVersion: Environment.OSVersion.ToString(),
            DotNetVersion: Environment.Version.ToString(),
            AppVersion: GetAppVersion(),
            ProcessArchitecture: System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount: Environment.ProcessorCount,
            MachineHash: HashMachineName(Environment.MachineName));
    }

    public static string FormatRuntimeInfo(DiagnosticRuntimeInfo runtimeInfo)
    {
        var webView2 = string.IsNullOrWhiteSpace(runtimeInfo.WebView2RuntimeVersion)
            ? "unavailable"
            : runtimeInfo.WebView2RuntimeVersion;

        return string.Join(Environment.NewLine, new[]
        {
            $"OS: {runtimeInfo.OsVersion}",
            $".NET: {runtimeInfo.DotNetVersion}",
            $"App: {runtimeInfo.AppVersion}",
            $"Architecture: {runtimeInfo.ProcessArchitecture}",
            $"Machine: {runtimeInfo.MachineHash}",
            $"Processors: {runtimeInfo.ProcessorCount}",
            $"WebView2: {webView2}",
        });
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
        string outputDirectory,
        DiagnosticRuntimeInfo runtimeInfo)
    {
        Directory.CreateDirectory(outputDirectory);
        var outputPath = CreateUniqueBundlePath(outputDirectory);

        using var zipStream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);
        var notes = new List<string>();

        // 1. Redacted settings
        var redactedSettings = RedactSettingsJson(settingsJson);
        await AddTextEntryAsync(
            archive,
            "settings-redacted.json",
            TruncateTextEntry(redactedSettings, "settings-redacted.json", notes));

        // 2. Runtime info
        await AddTextEntryAsync(archive, "runtime-info.txt", FormatRuntimeInfo(runtimeInfo));

        // 3. Diagnostic summary
        if (!string.IsNullOrWhiteSpace(diagnosticSummary))
        {
            await AddTextEntryAsync(
                archive,
                "diagnostic-summary.txt",
                TruncateTextEntry(RedactDiagnosticText(diagnosticSummary), "diagnostic-summary.txt", notes));
        }

        // 4. Recent log files
        var logFiles = CollectRecentLogFiles(logsDirectory);
        long bundledLogPayloadBytes = 0;
        var bundledLogFileCount = 0;
        foreach (var logFile in logFiles)
        {
            var entryName = $"logs/{Path.GetFileName(logFile)}";
            if (bundledLogFileCount >= MaxBundledLogFileCount)
            {
                notes.Add($"{Path.GetFileName(logFile)} skipped: log count limit {MaxBundledLogFileCount} reached");
                continue;
            }

            var logResult = await TryReadLogFileForBundleAsync(logFile);
            if (!logResult.Succeeded)
            {
                notes.Add($"{Path.GetFileName(logFile)} skipped: {logResult.Message}");
                continue;
            }

            if (bundledLogPayloadBytes + logResult.ByteCount > MaxBundledLogPayloadBytes)
            {
                notes.Add($"{Path.GetFileName(logFile)} skipped: bundle log payload limit {MaxBundledLogPayloadBytes} bytes reached");
                continue;
            }

            await AddTextEntryAsync(archive, entryName, RedactDiagnosticText(logResult.Content ?? string.Empty));
            bundledLogPayloadBytes += logResult.ByteCount;
            bundledLogFileCount++;
        }

        if (notes.Count > 0)
        {
            await AddTextEntryAsync(archive, "diagnostic-bundle-notes.txt", string.Join(Environment.NewLine, notes));
        }

        return outputPath;
    }

    private static async Task<DiagnosticLogReadResult> TryReadLogFileForBundleAsync(string logFile)
    {
        try
        {
            var info = new FileInfo(logFile);
            if (!info.Exists)
            {
                return DiagnosticLogReadResult.Failure("file no longer exists");
            }

            if (info.Length > MaxBundledLogFileBytes)
            {
                return DiagnosticLogReadResult.Failure($"file is {info.Length} bytes, limit is {MaxBundledLogFileBytes} bytes");
            }

            return DiagnosticLogReadResult.Success(await File.ReadAllTextAsync(logFile), info.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DiagnosticLogReadResult.Failure(ex.Message);
        }
    }

    private readonly record struct DiagnosticLogReadResult(bool Succeeded, string? Content, string Message, long ByteCount)
    {
        public static DiagnosticLogReadResult Success(string content, long byteCount) => new(true, content, string.Empty, byteCount);

        public static DiagnosticLogReadResult Failure(string message) => new(false, null, message, 0);
    }

    private static string CreateUniqueBundlePath(string outputDirectory)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var fileName = $"openclaw-diagnostics-{timestamp}-{suffix}.zip";
        return Path.Combine(outputDirectory, fileName);
    }

    private static async Task AddTextEntryAsync(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var writer = new StreamWriter(entry.Open());
        await writer.WriteAsync(content);
    }

    private static string TruncateTextEntry(string content, string entryName, List<string> notes)
    {
        if (Encoding.UTF8.GetByteCount(content) <= MaxDiagnosticTextEntryBytes)
        {
            return content;
        }

        var builder = new StringBuilder(capacity: Math.Min(content.Length, MaxDiagnosticTextEntryBytes));
        var byteCount = 0;
        foreach (var rune in content.EnumerateRunes())
        {
            var runeLength = rune.Utf8SequenceLength;
            if (byteCount + runeLength > MaxDiagnosticTextEntryBytes)
            {
                break;
            }

            builder.Append(rune);
            byteCount += runeLength;
        }

        builder.AppendLine();
        builder.AppendLine("<truncated>");
        notes.Add($"{entryName} truncated: entry exceeded {MaxDiagnosticTextEntryBytes} bytes");
        return builder.ToString();
    }

    [GeneratedRegex(@"(https?|wss?)://[^""'\s,}\]]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"(""(?:[^""]*(?:authorization|cookie|token|key|secret|password|credential)[^""]*)""\s*:\s*)""[^""]+""", RegexOptions.IgnoreCase)]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"(\b(?:authorization|cookie|token|key|secret|password|credential)\b\s*[=:]\s*)[^\s,;}\]]+", RegexOptions.IgnoreCase)]
    private static partial Regex KeyValueSecretPattern();

    [GeneratedRegex(@"(?im)^(\s*(?:Authorization|Cookie|Set-Cookie|X-Api-Key|Api-Key)\s*:\s*).+$")]
    private static partial Regex HeaderSecretPattern();

    private static string GetAppVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version is not null
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "unknown";
    }

    private static string HashMachineName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "unknown";
        }

        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(name));
        return Convert.ToHexString(bytes[..4]).ToLowerInvariant();
    }
}
