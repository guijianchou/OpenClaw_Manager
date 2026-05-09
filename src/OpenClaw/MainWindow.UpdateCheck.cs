// Copyright (c) Lanstack @openclaw. All rights reserved.

using OpenClaw.Helpers;
using OpenClaw.Services;

namespace OpenClaw;

public sealed partial class MainWindow
{
    private UpdateCheckService? _updateCheckService;

    private void InitializeUpdateCheck()
    {
        if (!App.Configuration.Settings.EnableUpdateCheck)
        {
            return;
        }

        if (!ShouldCheckForUpdate())
        {
            return;
        }

        _updateCheckService = new UpdateCheckService(
            new HttpClient(),
            "https://api.github.com/repos/guijianchou/OpenClaw_Manager/releases/latest");

        _ = CheckForUpdateAsync();
    }

    private bool ShouldCheckForUpdate()
    {
        var lastCheck = App.Configuration.Settings.LastUpdateCheckUtc;
        if (string.IsNullOrEmpty(lastCheck))
        {
            return true;
        }

        if (!DateTimeOffset.TryParse(lastCheck, out var lastCheckTime))
        {
            return true;
        }

        var intervalHours = Math.Max(1, App.Configuration.Settings.UpdateCheckIntervalHours);
        return DateTimeOffset.UtcNow - lastCheckTime > TimeSpan.FromHours(intervalHours);
    }

    private async Task CheckForUpdateAsync()
    {
        try
        {
            var currentVersion = new Version(
                AppMetadata.GetDisplayVersion());

            var result = await _updateCheckService!.CheckForUpdateAsync(currentVersion);

            if (result is null || !result.IsNewerAvailable)
            {
                RecordUpdateCheckTime();
                return;
            }

            // Skip if user already dismissed this version
            var dismissed = App.Configuration.Settings.DismissedUpdateVersion;
            if (!string.IsNullOrEmpty(dismissed) &&
                dismissed == result.LatestVersion.ToString())
            {
                RecordUpdateCheckTime();
                return;
            }

            RecordUpdateCheckTime();

            // Show update notification on UI thread
            DispatcherQueue.TryEnqueue(() => ShowUpdateNotification(result));
        }
        catch (Exception ex)
        {
            App.Logger.Warning($"Update check failed: {ex.Message}");
        }
    }

    private void ShowUpdateNotification(UpdateCheckResult result)
    {
        var message = string.Format(
            StringResources.UpdateAvailableMessage,
            result.LatestVersion);

        UpdateInfoBar.Message = message;

        if (!string.IsNullOrEmpty(result.ReleaseUrl) &&
            Uri.TryCreate(result.ReleaseUrl, UriKind.Absolute, out var uri))
        {
            UpdateReleaseLink.NavigateUri = uri;
        }

        UpdateInfoBar.IsOpen = true;
        UpdateInfoBar.Tag = result.LatestVersion.ToString();
    }

    private void OnUpdateInfoBarClosed(Microsoft.UI.Xaml.Controls.InfoBar sender,
        Microsoft.UI.Xaml.Controls.InfoBarClosedEventArgs args)
    {
        // Remember dismissed version so we don't nag again
        if (sender.Tag is string version && !string.IsNullOrEmpty(version))
        {
            App.Configuration.Settings.DismissedUpdateVersion = version;
            App.Configuration.SaveDeferred();
        }
    }

    private static void RecordUpdateCheckTime()
    {
        App.Configuration.Settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow.ToString("o");
        App.Configuration.SaveDeferred();
    }
}
