// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Security.Cryptography;
using System.Text;

namespace OpenClaw.Services;

public partial class WebViewService
{
    public static string GetUserDataFolderForEnvironment(string environmentName)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenClaw",
            "WebView2Data");

        return Path.Combine(root, BuildEnvironmentFolderName(environmentName));
    }

    public static void DeleteUserDataFolderForEnvironment(string environmentName)
    {
        try
        {
            var folder = GetUserDataFolderForEnvironment(environmentName);
            if (!Directory.Exists(folder))
            {
                return;
            }

            Directory.Delete(folder, recursive: true);
            App.Logger.Info($"Deleted WebView2 profile folder for environment '{environmentName}'.");
        }
        catch (Exception ex)
        {
            App.Logger.Warning($"Failed to delete WebView2 profile folder for environment '{environmentName}': {ex.Message}");
        }
    }

    public static void TryMoveUserDataFolderToRenamedEnvironment(string originalEnvironmentName, string renamedEnvironmentName)
    {
        if (string.IsNullOrWhiteSpace(originalEnvironmentName) ||
            string.IsNullOrWhiteSpace(renamedEnvironmentName) ||
            string.Equals(originalEnvironmentName, renamedEnvironmentName, StringComparison.Ordinal))
        {
            return;
        }

        var sourceFolder = GetUserDataFolderForEnvironment(originalEnvironmentName);
        var targetFolder = GetUserDataFolderForEnvironment(renamedEnvironmentName);

        try
        {
            if (!Directory.Exists(sourceFolder) || Directory.Exists(targetFolder))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetFolder)!);
            Directory.Move(sourceFolder, targetFolder);
            App.Logger.Info($"Moved WebView2 profile folder from '{originalEnvironmentName}' to '{renamedEnvironmentName}'.");
        }
        catch (Exception ex)
        {
            App.Logger.Warning($"Failed to move WebView2 profile folder from '{originalEnvironmentName}' to '{renamedEnvironmentName}': {ex.Message}");
        }
    }

    private static string BuildEnvironmentFolderName(string environmentName)
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
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..8];
        return $"{sanitized}_{hash}";
    }
}
