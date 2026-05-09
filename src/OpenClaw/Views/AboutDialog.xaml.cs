// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Xaml.Controls;
using OpenClaw.Helpers;
using OpenClaw.Services;
using Microsoft.UI.Xaml;

namespace OpenClaw.Views;

/// <summary>
/// About dialog displaying application name, version, and links.
/// </summary>
public sealed partial class AboutDialog : ContentDialog
{
    private const string ReleaseApiUrl = "https://api.github.com/repos/guijianchou/OpenClaw_Manager/releases/latest";

    public AboutDialog()
    {
        this.InitializeComponent();
        VersionText.Text = $"Version {AppMetadata.GetDisplayVersion()}";
    }

    private async void OnCheckForUpdatesClick(object sender, RoutedEventArgs e)
    {
        ManualUpdateCheckButton.IsEnabled = false;
        ManualUpdateReleaseLink.Visibility = Visibility.Collapsed;
        ManualUpdateStatusText.Text = StringResources.AboutCheckingForUpdates;

        try
        {
            using var httpClient = new HttpClient();
            var updateCheckService = new UpdateCheckService(httpClient, ReleaseApiUrl);
            var currentVersion = new Version(AppMetadata.GetDisplayVersion());
            var result = await updateCheckService.CheckForUpdateAsync(currentVersion);

            if (result is null)
            {
                ManualUpdateStatusText.Text = StringResources.AboutUpdateCheckFailed;
                return;
            }

            if (!result.IsNewerAvailable)
            {
                ManualUpdateStatusText.Text = StringResources.AboutUpdateCheckNoUpdate;
                return;
            }

            ManualUpdateStatusText.Text = string.Format(
                StringResources.UpdateAvailableMessage,
                result.LatestVersion);

            if (!string.IsNullOrWhiteSpace(result.ReleaseUrl) &&
                Uri.TryCreate(result.ReleaseUrl, UriKind.Absolute, out var releaseUri))
            {
                ManualUpdateReleaseLink.NavigateUri = releaseUri;
            }

            ManualUpdateReleaseLink.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            App.Logger.Warning($"Manual update check failed: {ex.Message}");
            ManualUpdateStatusText.Text = StringResources.AboutUpdateCheckFailed;
        }
        finally
        {
            ManualUpdateCheckButton.IsEnabled = true;
        }
    }
}
