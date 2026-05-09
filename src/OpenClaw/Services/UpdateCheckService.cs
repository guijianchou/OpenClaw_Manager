// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenClaw.Services;

/// <summary>
/// Result of an update check against GitHub Releases.
/// </summary>
public sealed record UpdateCheckResult(
    Version LatestVersion,
    bool IsNewerAvailable,
    string ReleaseUrl);

/// <summary>
/// Checks for new releases by querying the GitHub Releases API.
/// </summary>
public sealed class UpdateCheckService
{
    private static readonly Regex VersionTagPattern = new(@"^v?(\d+\.\d+\.\d+)$", RegexOptions.Compiled);
    private readonly HttpClient _httpClient;
    private readonly string _releaseApiUrl;

    public UpdateCheckService(HttpClient httpClient, string releaseApiUrl)
    {
        _httpClient = httpClient;
        _releaseApiUrl = releaseApiUrl;
    }

    /// <summary>
    /// Checks the latest GitHub release against the current version.
    /// Returns null if the check fails (network error, malformed response, pre-release).
    /// </summary>
    public async Task<UpdateCheckResult?> CheckForUpdateAsync(Version currentVersion)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _releaseApiUrl);
            request.Headers.Add("User-Agent", "OpenClaw-Manager");
            request.Headers.Add("Accept", "application/vnd.github+json");

            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            // Skip pre-releases
            if (root.TryGetProperty("prerelease", out var prereleaseElement) &&
                prereleaseElement.GetBoolean())
            {
                return null;
            }

            if (!root.TryGetProperty("tag_name", out var tagElement))
            {
                return null;
            }

            var tagName = tagElement.GetString();
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return null;
            }

            var match = VersionTagPattern.Match(tagName);
            if (!match.Success)
            {
                return null;
            }

            var latestVersion = Version.Parse(match.Groups[1].Value);
            var releaseUrl = root.TryGetProperty("html_url", out var urlElement)
                ? urlElement.GetString() ?? string.Empty
                : string.Empty;

            return new UpdateCheckResult(
                latestVersion,
                latestVersion > currentVersion,
                releaseUrl);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }
}
