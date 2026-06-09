// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Security.Cryptography;
using System.Text;

namespace OpenClaw.Services;

public partial class WebViewService
{
    private const string ProfileIdentityFileName = ".openclaw-profile-identity";
    private const int DeleteProfileAttemptCount = 3;
    private static readonly TimeSpan DeleteProfileRetryDelay = TimeSpan.FromMilliseconds(150);

    public static string GetUserDataFolderForEnvironment(string environmentName)
        => GetUserDataFolderForEnvironment(environmentName, gatewayUrl: null);

    public static string GetUserDataFolderForEnvironment(string environmentName, string? gatewayUrl)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenClaw",
            "WebView2Data");

        return Path.Combine(root, BuildEnvironmentFolderName(environmentName, gatewayUrl));
    }

    public static void DeleteUserDataFolderForEnvironment(string environmentName, IAppLogger? logger = null)
        => DeleteUserDataFolderForEnvironment(environmentName, gatewayUrl: null, logger);

    public static void DeleteUserDataFolderForEnvironment(string environmentName, string? gatewayUrl, IAppLogger? logger = null)
        => DeleteUserDataFolderForEnvironmentCore(environmentName, gatewayUrl, logger);

    public static Task DeleteUserDataFolderForEnvironmentAsync(
        string environmentName,
        IAppLogger? logger = null,
        CancellationToken cancellationToken = default)
        => DeleteUserDataFolderForEnvironmentAsync(environmentName, gatewayUrl: null, logger, cancellationToken);

    public static async Task DeleteUserDataFolderForEnvironmentAsync(
        string environmentName,
        string? gatewayUrl,
        IAppLogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var folder = GetUserDataFolderForEnvironment(environmentName, gatewayUrl);
        if (!Directory.Exists(folder))
        {
            return;
        }

        Exception? lastError = null;
        for (var attempt = 1; attempt <= DeleteProfileAttemptCount; attempt++)
        {
            try
            {
                await Task.Run(() => Directory.Delete(folder, recursive: true), cancellationToken);
                logger?.Info($"Deleted WebView2 profile folder for environment '{environmentName}'.");
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastError = ex;
                if (attempt < DeleteProfileAttemptCount)
                {
                    await Task.Delay(DeleteProfileRetryDelay * attempt, cancellationToken);
                }
            }
        }

        var message = $"Failed to delete WebView2 profile folder for environment '{environmentName}': {lastError?.Message}";
        logger?.Warning(message);
        throw new IOException(message, lastError);
    }

    private static void DeleteUserDataFolderForEnvironmentCore(string environmentName, string? gatewayUrl, IAppLogger? logger = null)
    {
        var folder = GetUserDataFolderForEnvironment(environmentName, gatewayUrl);
        if (!Directory.Exists(folder))
        {
            return;
        }

        try
        {
            Directory.Delete(folder, recursive: true);
            logger?.Info($"Deleted WebView2 profile folder for environment '{environmentName}'.");
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var message = $"Failed to delete WebView2 profile folder for environment '{environmentName}': {ex.Message}";
            logger?.Warning(message);
            throw new IOException(message, ex);
        }
    }

    public static void TryMoveUserDataFolderToRenamedEnvironment(
        string originalEnvironmentName,
        string renamedEnvironmentName,
        string? gatewayUrl,
        IAppLogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(originalEnvironmentName) ||
            string.IsNullOrWhiteSpace(renamedEnvironmentName) ||
            string.Equals(originalEnvironmentName, renamedEnvironmentName, StringComparison.Ordinal))
        {
            return;
        }

        var sourceFolder = GetUserDataFolderForEnvironment(originalEnvironmentName, gatewayUrl);
        var targetFolder = GetUserDataFolderForEnvironment(renamedEnvironmentName, gatewayUrl);
        if (string.Equals(sourceFolder, targetFolder, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            if (!Directory.Exists(sourceFolder) || Directory.Exists(targetFolder))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetFolder)!);
            Directory.Move(sourceFolder, targetFolder);
            logger?.Info($"Moved WebView2 profile folder from '{originalEnvironmentName}' to '{renamedEnvironmentName}'.");
        }
        catch (Exception ex)
        {
            logger?.Warning($"Failed to move WebView2 profile folder from '{originalEnvironmentName}' to '{renamedEnvironmentName}': {ex.Message}");
        }
    }

    private static async Task MigrateLegacyUserDataFolderIfNeededAsync(
        string environmentName,
        string? gatewayUrl,
        IAppLogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gatewayUrl))
        {
            return;
        }

        var profileFolder = GetUserDataFolderForEnvironment(environmentName, gatewayUrl);
        if (Directory.Exists(profileFolder))
        {
            return;
        }

        foreach (var legacyFolder in EnumerateLegacyProfileFolders(environmentName, gatewayUrl, profileFolder))
        {
            var legacyIdentity = await TryReadProfileIdentityMarkerAsync(legacyFolder, cancellationToken);
            if (!string.IsNullOrWhiteSpace(legacyIdentity) &&
                !GatewayUrlIdentity.ProfileIdentityMarkerMatches(legacyIdentity, gatewayUrl))
            {
                logger?.Info($"Skipped legacy WebView2 profile migration for environment '{environmentName}' because the legacy profile URL identity is different.");
                continue;
            }

            try
            {
                await Task.Run(
                    () =>
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(profileFolder)!);
                        Directory.Move(legacyFolder, profileFolder);
                    },
                    cancellationToken);
                logger?.Info($"Migrated WebView2 profile folder for environment '{environmentName}' to stable URL-scoped profile identity.");
                return;
            }
            catch (Exception ex)
            {
                logger?.Warning($"Failed to migrate WebView2 profile folder for environment '{environmentName}': {ex.Message}");
                return;
            }
        }
    }

    private static async Task WriteProfileIdentityMarkerAsync(
        string profileFolder,
        string? gatewayUrl,
        IAppLogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gatewayUrl))
        {
            return;
        }

        try
        {
            var markerPath = Path.Combine(profileFolder, ProfileIdentityFileName);
            await File.WriteAllTextAsync(
                markerPath,
                GatewayUrlIdentity.CreateProfileIdentityMarker(gatewayUrl),
                cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.Warning($"Failed to write WebView2 profile identity marker: {ex.Message}");
        }
    }

    private static async Task<string?> TryReadProfileIdentityMarkerAsync(
        string profileFolder,
        CancellationToken cancellationToken)
    {
        var markerPath = Path.Combine(profileFolder, ProfileIdentityFileName);
        if (!File.Exists(markerPath))
        {
            return null;
        }

        try
        {
            return (await File.ReadAllTextAsync(markerPath, cancellationToken)).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string BuildEnvironmentFolderName(string environmentName, string? gatewayUrl)
    {
        if (!string.IsNullOrWhiteSpace(gatewayUrl))
        {
            var hash = GatewayUrlIdentity.CreateProfileIdentityHash(gatewayUrl)[..16];
            return $"profile_{hash}";
        }

        return BuildLegacyEnvironmentFolderName(environmentName, gatewayUrl: null);
    }

    private static IEnumerable<string> EnumerateLegacyProfileFolders(
        string environmentName,
        string gatewayUrl,
        string profileFolder)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenClaw",
            "WebView2Data");
        var candidates = new[]
        {
            Path.Combine(root, BuildLegacyEnvironmentFolderName(environmentName, gatewayUrl)),
            Path.Combine(root, BuildLegacyEnvironmentFolderName(environmentName, gatewayUrl: null)),
        };

        return candidates
            .Where(folder => !string.Equals(folder, profileFolder, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists);
    }

    private static string BuildLegacyEnvironmentFolderName(string environmentName, string? gatewayUrl)
    {
        var normalized = string.IsNullOrWhiteSpace(environmentName) ? "default" : environmentName.Trim();
        var sanitized = new string(normalized
            .Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)
            .ToArray())
            .Trim();

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "default";
        }

        sanitized = sanitized.Length > 48 ? sanitized[..48] : sanitized;
        var profileIdentity = string.IsNullOrWhiteSpace(gatewayUrl)
            ? normalized
            : $"{normalized}\n{NormalizeGatewayUrlForLegacyProfileIdentity(gatewayUrl)}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profileIdentity)))[..8];
        return $"{sanitized}_{hash}";
    }

    private static string NormalizeGatewayUrlForLegacyProfileIdentity(string gatewayUrl)
    {
        if (!Uri.TryCreate(gatewayUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return gatewayUrl.Trim();
        }

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
            UserName = string.Empty,
            Password = string.Empty,
        };
        return builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }
}
